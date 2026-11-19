import { test, expect, Page, APIRequestContext, Browser } from '@playwright/test';
import { MANAGER_PIN, addCategory, addItem, addStaff, ok, ownerApi, setPin, signIn, uid } from './support/pos';

// 1.9 test cases E1-E6: shift and expenses, discount limit with manager PIN, reprint switch, void, tips, bill on WhatsApp.
// Runs in file order: the shift is opened first, because cash tips go into the open shift.

test.describe.configure({ mode: 'serial' });

// window.open is recorded instead of opening print or WhatsApp windows.
async function staffPage(browser: Browser, email: string, password: string) {
  const context = await browser.newContext();
  await context.addInitScript(() => {
    (window as unknown as { __opened: string[] }).__opened = [];
    window.open = ((url?: string | URL) => { (window as unknown as { __opened: string[] }).__opened.push(String(url)); return null; }) as typeof window.open;
  });
  const page = await context.newPage();
  await signIn(page, email, password);
  return page;
}
const opened = (page: Page) => page.evaluate(() => (window as unknown as { __opened: string[] }).__opened);

async function thali(api: APIRequestContext, id: string) {
  const category = await addCategory(api, `Bills ${id}`);
  return addItem(api, { name: `Thali ${id}`, price: 200, categoryId: category.id });
}

async function apiOrder(api: APIRequestContext, itemId: number, customer: string) {
  return ok(await api.post('/api/orders', {
    data: { orderType: 'Takeaway', customerName: customer, clientOrderId: crypto.randomUUID(), items: [{ menuItemId: itemId, quantity: 1 }] },
  }));
}

async function openBillFromOrders(page: Page, token: number | string) {
  await page.goto('/orders');
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: String(token), exact: true }) });
  await row.getByRole('button', { name: 'Bill', exact: true }).click();
  return page.getByRole('dialog');
}

async function billWithDiscount(page: Page, token: number | string, percent: string) {
  const dialog = await openBillFromOrders(page, token);
  await dialog.getByLabel('Percent off').check();
  await dialog.getByLabel('Discount (%)').fill(percent);
  await dialog.getByRole('button', { name: 'Make bill' }).click();
  return dialog;
}

let api: APIRequestContext;
let cashier: { email: string; password: string };
let manager: { email: string; password: string };
let item: { id: number; name: string };
let id: string;

test.beforeAll(async () => {
  api = await ownerApi();
  id = uid();
  cashier = await addStaff(api, 'cashier', `Ravi ${id}`);
  const m = await addStaff(api, 'manager', `Meena ${id}`);
  await setPin(api, m.id, MANAGER_PIN);
  manager = m;
  item = await thali(api, id);
});

test('E6 shift and expenses: only money from the till lowers the expected cash', async ({ browser }) => {
  const page = await staffPage(browser, cashier.email, cashier.password);
  await page.goto('/day-close');
  // Fresh database in every run, so no shift is open yet.
  await page.getByLabel('Opening cash').fill('2000');
  await page.getByRole('button', { name: 'Open shift' }).click();
  await expect(page.getByText('Shift opened.')).toBeVisible();
  await expect(page.getByText('Current shift')).toBeVisible();

  await page.goto('/expenses');
  await page.getByLabel('Category').selectOption({ label: 'Milk' });
  await page.getByLabel('Amount').fill('120');
  await page.getByLabel('Paid from').selectOption({ label: 'Till (cash drawer)' });
  await page.getByLabel('Note').fill('Amul 4 L');
  await page.getByRole('button', { name: 'Save expense' }).click();
  await expect(page.getByText('Expense of ₹120.00 saved.')).toBeVisible();

  await page.getByLabel('Category').selectOption({ label: 'Vegetables' });
  await page.getByLabel('Amount').fill('500');
  await page.getByLabel('Paid from').selectOption({ index: 1 });
  await page.getByLabel('Note').fill('Weekly sabzi');
  await page.getByRole('button', { name: 'Save expense' }).click();
  await expect(page.getByText('Expense of ₹500.00 saved.')).toBeVisible();
  await expect(page.getByText(/Total ₹620\.00 · from the till ₹120\.00/)).toBeVisible();
});

test('E1 discount above the cashier limit needs a manager PIN', async ({ browser }) => {
  await ok(await api.put('/api/admin/discount-limit', { data: { cashierMaxDiscountPercent: 10 } }));
  try {
    const page = await staffPage(browser, cashier.email, cashier.password);

    const small = await apiOrder(api, item.id, `Small ${id}`);
    let dialog = await billWithDiscount(page, small.tokenNumber, '10');
    await expect(dialog.getByRole('heading', { name: /^Bill / })).toBeVisible();
    await expect(dialog.getByText('₹20.00').first()).toBeVisible();
    await page.keyboard.press('Escape');

    const big = await apiOrder(api, item.id, `Big ${id}`);
    dialog = await billWithDiscount(page, big.tokenNumber, '20');
    await expect(dialog.getByText('This discount is above the cashier limit. A manager PIN is needed.').first()).toBeVisible();
    await dialog.getByLabel('Manager PIN').fill('1111');
    await dialog.getByRole('button', { name: 'Make bill' }).click();
    await expect(dialog.getByRole('alert')).toContainText(/wrong/i);
    await dialog.getByLabel('Manager PIN').fill(MANAGER_PIN);
    await dialog.getByRole('button', { name: 'Make bill' }).click();
    await expect(dialog.getByRole('heading', { name: /^Bill / })).toBeVisible();
    await expect(dialog.getByText('₹40.00').first()).toBeVisible();

    const boss = await staffPage(browser, manager.email, manager.password);
    const third = await apiOrder(api, item.id, `Boss ${id}`);
    dialog = await billWithDiscount(boss, third.tokenNumber, '20');
    await expect(dialog.getByRole('heading', { name: /^Bill / })).toBeVisible();
  } finally {
    await ok(await api.put('/api/admin/discount-limit', { data: { cashierMaxDiscountPercent: null } }));
  }
});

test('E2 reprint asks a cashier for the PIN unless the owner turns it off', async ({ browser }) => {
  const order = await apiOrder(api, item.id, `Reprint ${id}`);
  const bill = await ok(await api.post(`/api/orders/${order.orderId}/bill`, { data: {} }));
  await ok(await api.post(`/api/bills/${bill.id}/printed`));
  try {
    const page = await staffPage(browser, cashier.email, cashier.password);
    let dialog = await openBillFromOrders(page, order.tokenNumber);
    await dialog.getByRole('button', { name: 'Print bill' }).click();
    await expect(dialog.getByLabel('Manager PIN to print again')).toBeVisible();
    expect(await opened(page)).toEqual([]);

    await ok(await api.put('/api/admin/approval-rules', { data: { reprintNeedsPin: false } }));
    dialog = await openBillFromOrders(page, order.tokenNumber);
    await dialog.getByRole('button', { name: 'Print bill' }).click();
    await expect.poll(() => opened(page)).toContain(`/print/bill/${bill.id}?auto=1`);
    await expect(dialog.getByLabel('Manager PIN to print again')).toHaveCount(0);
  } finally {
    await ok(await api.put('/api/admin/approval-rules', { data: { reprintNeedsPin: true } }));
  }
});

test('E3 a cashier voids a bill with the manager PIN', async ({ browser }) => {
  const order = await apiOrder(api, item.id, `Void ${id}`);
  await ok(await api.post(`/api/orders/${order.orderId}/bill`, { data: {} }));
  const page = await staffPage(browser, cashier.email, cashier.password);
  const dialog = await openBillFromOrders(page, order.tokenNumber);
  await dialog.getByRole('button', { name: 'Void bill' }).click();
  await dialog.getByLabel('Reason for void').fill('Wrong table');
  await dialog.getByRole('button', { name: 'Yes, void this bill' }).click();
  await expect(dialog.getByText('Enter the manager PIN.')).toBeVisible();
  await dialog.getByLabel('Manager PIN').fill(MANAGER_PIN);
  await dialog.getByRole('button', { name: 'Yes, void this bill' }).click();
  await expect(dialog.getByRole('button', { name: 'Make bill' })).toBeVisible();
});

test('E4 a tip is saved for the waiter and does not change the bill', async ({ browser }) => {
  const order = await apiOrder(api, item.id, `Tip ${id}`);
  await ok(await api.post(`/api/orders/${order.orderId}/bill`, { data: {} }));
  const page = await staffPage(browser, cashier.email, cashier.password);
  const dialog = await openBillFromOrders(page, order.tokenNumber);
  await dialog.getByRole('button', { name: 'Add tip' }).click();
  await dialog.getByLabel('Tip amount').fill('50');
  await dialog.getByLabel('Waiter').fill(`Suresh ${id}`);
  await dialog.getByRole('button', { name: 'Save tip' }).click();
  await expect(page.getByText(`Tip of ₹50.00 saved for Suresh ${id}.`)).toBeVisible();
  await expect(dialog.getByText('₹200.00').first()).toBeVisible();
});

test('E5 send on WhatsApp opens the customer chat with the bill', async ({ browser }) => {
  const page = await staffPage(browser, cashier.email, cashier.password);
  await page.goto('/pos');
  await page.getByRole('button', { name: 'Takeaway', exact: true }).click();
  await page.getByLabel('Customer name').fill(`Kiran ${id}`);
  await page.getByLabel('Phone (optional)').fill('9898012345');
  await page.getByRole('button', { name: `Add ${item.name}`, exact: true }).click();
  await page.getByRole('button', { name: 'Place order' }).click();
  await page.getByRole('button', { name: 'Bill & pay' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Make bill' }).click();
  await dialog.getByRole('button', { name: 'Send on WhatsApp' }).click();
  await expect.poll(async () => (await opened(page)).find((u) => u.startsWith('https://wa.me/')) ?? '')
    .toMatch(/^https:\/\/wa\.me\/919898012345\?text=/);
  const url = (await opened(page)).find((u) => u.startsWith('https://wa.me/'))!;
  expect(decodeURIComponent(url)).toContain(`Thali ${id}`);
});
