import { test, expect, APIRequestContext, request } from '@playwright/test';
import { BASE_URL, MANAGER_PIN, STAFF_PASSWORD, ok, ownerApi, uid } from './support/pos';

// Sample data for trying the POS by hand (not a test): a full menu, staff, and a busy day of orders in every state,
// with bills, split payments, discounts, tips, guest feedback, a shift and expenses.
// Runs only when SEED=1:  scripts/seed-aws.ps1 (a live server) or scripts/seed-local.ps1 (the E2E Docker copy).
// Safe to run again: it reuses categories and items with the same name, and adds another day's worth of orders.

test.skip(process.env.SEED !== '1', 'sample data only when SEED=1');
test.setTimeout(15 * 60 * 1000);

type Item = { id: number; name: string; price: number };

const MENU: { category: string; items: [string, number, boolean][] }[] = [
  { category: 'Thali', items: [['Gujarati Thali', 220, true], ['Kathiyawadi Thali', 260, true], ['Mini Thali', 150, true], ['Punjabi Thali', 240, true]] },
  { category: 'Snacks', items: [['Khaman Dhokla', 60, true], ['Samosa (2 pcs)', 40, true], ['Pav Bhaji', 120, true], ['Vada Pav', 30, true], ['Paneer Tikka', 180, true], ['Chicken Tikka', 220, false]] },
  { category: 'Main course', items: [['Dal Makhani', 160, true], ['Paneer Butter Masala', 190, true], ['Veg Biryani', 170, true], ['Chicken Biryani', 230, false], ['Butter Naan', 40, true], ['Jeera Rice', 110, true]] },
  { category: 'Drinks', items: [['Masala Tea', 20, true], ['Cold Coffee', 80, true], ['Chaas', 30, true], ['Sweet Lassi', 60, true], ['Fresh Lime Soda', 50, true], ['Mineral Water', 20, true]] },
  { category: 'Desserts', items: [['Gulab Jamun (2 pcs)', 50, true], ['Rasmalai', 70, true], ['Kulfi', 60, true], ['Brownie with Ice Cream', 120, true]] },
];

const NAMES = ['Ravi', 'Priya', 'Amit', 'Neha', 'Kiran', 'Sejal', 'Harsh', 'Pooja', 'Vikram', 'Meera', 'Jay', 'Dhruv', 'Riya', 'Mitesh', 'Hetal'];
const COMMENTS: [number, string][] = [
  [5, 'Thali was excellent, will come again'], [4, 'Good food, service a bit slow'], [5, 'Best dhokla in town'],
  [3, 'Food was fine, too spicy for kids'], [2, 'Waited 30 minutes for the order'], [5, 'Lovely staff and clean place'],
  [4, 'Paneer tikka very tasty'], [1, 'Food was cold'], [5, 'Great value for money'], [4, 'Lassi was perfect'],
];

let n = 0;
const pick = <T>(list: T[]) => list[n++ % list.length];
const pause = (ms: number) => new Promise((r) => setTimeout(r, ms));

async function menu(api: APIRequestContext): Promise<Item[]> {
  const categories: { id: number; name: string }[] = await ok(await api.get('/api/admin/menu/categories'));
  const existing: { id: number; name: string; price: number }[] = await ok(await api.get('/api/admin/menu/items'));
  const items: Item[] = [];
  let order = 10;
  for (const group of MENU) {
    let category = categories.find((c) => c.name === group.category);
    if (!category) category = await ok(await api.post('/api/admin/menu/categories', { data: { name: group.category, sortOrder: order, isActive: true } }));
    order += 10;
    for (const [name, price, isVeg] of group.items) {
      let item = existing.find((i) => i.name === name);
      if (!item) {
        item = await ok(await api.post('/api/admin/menu/items', {
          data: { name, price, isVeg, categoryId: category!.id, description: null, isAvailable: true, taxRate: 5, sortOrder: 50 },
        }));
      }
      items.push({ id: item!.id, name, price: Number(item!.price ?? price) });
    }
  }
  return items;
}

async function staff(api: APIRequestContext) {
  const tag = uid();
  const people: { role: string; name: string }[] = [
    { role: 'manager', name: 'Suresh (manager)' }, { role: 'cashier', name: 'Anita (cashier)' },
    { role: 'waiter', name: 'Ramesh (waiter)' }, { role: 'kitchen', name: 'Kitchen screen' },
  ];
  const made: { role: string; email: string }[] = [];
  for (const p of people) {
    const email = `${p.role}-${tag}@demo.local`;
    const res = await api.post('/api/staff', { data: { email, displayName: p.name, role: p.role, password: STAFF_PASSWORD, isDevice: false } });
    if (!res.ok()) continue;
    const created = await res.json();
    if (p.role === 'manager') await api.put(`/api/staff/${created.id}/pin`, { data: { pin: MANAGER_PIN } });
    made.push({ role: p.role, email });
  }
  return made;
}

test('sample data', async () => {
  const api = await ownerApi();
  const items = await menu(api);
  const people = await staff(api);

  // Free tables for dine-in; add a few if the restaurant has none.
  let tables: { id: number; tableNumber: string; status: string }[] = await ok(await api.get('/api/tables'));
  if (tables.length < 6) {
    for (let i = 1; i <= 8; i++) await api.post('/api/admin/tables', { data: { tableNumber: `T${i}`, isActive: true, sortOrder: i } });
    tables = await ok(await api.get('/api/tables'));
  }
  const freeTables = () => tables.filter((t) => t.status === 'Free').map((t) => t.id);

  // The day's till, guest feedback on bills, and a review link.
  await api.post('/api/shifts/open', { data: { openingCash: 2000 } });
  await api.put('/api/admin/feedback-setting', { data: { enabled: true } });

  const lines = (count: number) => {
    const chosen = new Map<number, number>();
    for (let i = 0; i < count; i++) {
      const item = pick(items);
      chosen.set(item.id, (chosen.get(item.id) ?? 0) + 1 + (i % 3 === 0 ? 1 : 0));
    }
    return [...chosen].map(([menuItemId, quantity]) => ({ menuItemId, quantity }));
  };

  async function place(dineIn: boolean) {
    let table: number | undefined;
    if (dineIn) {
      tables = await ok(await api.get('/api/tables'));
      table = freeTables()[0];
    }
    const data = table
      ? { orderType: 'DineIn', table, items: lines(2 + (n % 4)), clientOrderId: crypto.randomUUID() }
      : { orderType: 'Takeaway', customerName: pick(NAMES), items: lines(1 + (n % 3)), clientOrderId: crypto.randomUUID() };
    const placed = await ok(await api.post('/api/orders', { data }));
    await pause(150);
    return placed.orderId as number;
  }
  const setStatus = async (id: number, status: string) => ok(await api.put(`/api/orders/${id}/status`, { data: { status } }));

  async function billAndPay(orderId: number, kind: number) {
    const discount = kind % 5 === 0 ? { discountPercent: 10 } : {};
    const bill = await ok(await api.post(`/api/orders/${orderId}/bill`, { data: discount }));
    const total = Number(bill.total);
    let paid = bill;
    if (kind % 4 === 1) {
      paid = await ok(await api.post(`/api/bills/${bill.id}/payments`, { data: { method: 'upi', amount: total, version: bill.version } }));
    } else if (kind % 4 === 2) {
      paid = await ok(await api.post(`/api/bills/${bill.id}/payments`, { data: { method: 'card', amount: total, version: bill.version } }));
    } else if (kind % 4 === 3 && total > 100) {
      const half = Math.round(total / 2);
      const first = await ok(await api.post(`/api/bills/${bill.id}/payments`, { data: { method: 'cash', amount: half, tendered: half, version: bill.version } }));
      paid = await ok(await api.post(`/api/bills/${bill.id}/payments`, { data: { method: 'upi', amount: Number((total - half).toFixed(2)), version: first.version } }));
    } else {
      const tendered = Math.ceil(total / 100) * 100;
      paid = await ok(await api.post(`/api/bills/${bill.id}/payments`, { data: { method: 'cash', amount: total, tendered, version: bill.version } }));
    }
    expect(paid.status).toBe('Paid');
    return bill.id as number;
  }

  const guest = await request.newContext({ baseURL: BASE_URL });
  const paidBills: number[] = [];

  // 1. Finished orders: cooked, billed, paid, served.
  for (let k = 0; k < 24; k++) {
    const id = await place(k % 2 === 0);
    await setStatus(id, 'Preparing');
    await setStatus(id, 'Ready');
    const billId = await billAndPay(id, k);
    paidBills.push(billId);
    const order = await ok(await api.get(`/api/orders/${id}`));
    if (order.status === 'Ready') await setStatus(id, 'Completed');
    // Most guests have left; leave a few paid tables showing Paid.
    if (order.tableId && k < 20) await api.post(`/api/tables/${order.tableId}/clear`);
    if (k % 6 === 0) await api.post(`/api/bills/${billId}/tips`, { data: { method: k % 12 === 0 ? 'cash' : 'upi', amount: 50, waiterName: 'Ramesh' } });
  }

  // 2. Guest feedback on some of those bills.
  for (let k = 0; k < Math.min(COMMENTS.length, paidBills.length); k++) {
    const link = await api.get(`/api/bills/${paidBills[k * 2 % paidBills.length]}/feedback-link`);
    if (!link.ok()) continue;
    const { token } = await link.json();
    const [rating, comment] = COMMENTS[k];
    await guest.post(`/api/feedback/${token}`, { data: { rating, comment } });
    await pause(2500); // the guest pages allow only a few posts a minute
  }

  // 3. Orders still in the kitchen, ready to collect, billed but not paid, and cancelled.
  for (let k = 0; k < 4; k++) await place(k % 2 === 0); // new
  for (let k = 0; k < 4; k++) await setStatus(await place(k % 2 === 1), 'Preparing');
  for (let k = 0; k < 3; k++) {
    const id = await place(false);
    await setStatus(id, 'Ready');
  }
  for (let k = 0; k < 2; k++) {
    const id = await place(true);
    await setStatus(id, 'Ready');
    await ok(await api.post(`/api/orders/${id}/bill`, { data: {} }));
  }
  for (let k = 0; k < 2; k++) {
    const id = await place(false);
    const order = await ok(await api.get(`/api/orders/${id}`));
    await ok(await api.post(`/api/orders/${id}/cancel`, { data: { reason: k === 0 ? 'Customer left' : 'Wrong item', version: order.version } }));
  }

  // 4. The day's expenses.
  const expenses: [string, number, string, string][] = [
    ['Milk', 480, 'Till', 'Amul 12 L'], ['Vegetables', 1250, 'Till', 'Morning market'],
    ['Gas', 1100, 'Other', 'Cylinder'], ['Repairs', 350, 'Other', 'Mixer repair'],
  ];
  for (const [category, amount, paidFrom, note] of expenses) await api.post('/api/expenses', { data: { category, amount, paidFrom, note } });

  console.log(`Sample data ready on ${BASE_URL}: ${items.length} menu items, ${people.length} staff (password ${STAFF_PASSWORD}, manager PIN ${MANAGER_PIN}), 41 orders, ${paidBills.length} paid bills.`);
  for (const p of people) console.log(`  ${p.role}: ${p.email}`);
});
