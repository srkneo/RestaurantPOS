import { test, expect, Page, APIRequestContext } from '@playwright/test';
import { addCategory, addItem, addStaff, ok, ownerApi, pageAs, uid } from './support/pos';

// v2.2 loyalty: the owner turns it on, guests join only with consent, bills earn points, the next bill spends them.
test.describe.configure({ mode: 'serial' });

let api: APIRequestContext;
let cashier: { email: string; password: string };
let item: { id: number };
let id: string;
let name: string;
let phone: string;

test.beforeAll(async () => {
  api = await ownerApi();
  id = uid();
  name = `Asha ${id}`;
  phone = '9' + String(Date.now()).slice(-9);
  cashier = await addStaff(api, 'cashier', `Ravi ${id}`);
  const category = await addCategory(api, `Loyalty ${id}`);
  item = await addItem(api, { name: `Loyalty Thali ${id}`, price: 200, categoryId: category.id });
});

test.afterAll(async () => {
  await ok(await api.put('/api/customers/settings', { data: { enabled: false, rupeesPerPoint: 100, pointValue: 1 } }));
});

async function billFor(guestPhone: string, customer: string) {
  const order = await ok(await api.post('/api/orders', {
    data: { orderType: 'Takeaway', customerName: customer, customerPhone: guestPhone, clientOrderId: crypto.randomUUID(), items: [{ menuItemId: item.id, quantity: 1 }] },
  }));
  const bill = await ok(await api.post(`/api/orders/${order.orderId}/bill`, { data: {} }));
  return { order, bill };
}

async function openBill(page: Page, token: number) {
  await page.goto('/orders');
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: String(token), exact: true }) });
  await row.getByRole('button', { name: 'Bill', exact: true }).click();
  return page.getByRole('dialog');
}

test('L1 owner turns loyalty on and adds a guest only with consent', async ({ browser }) => {
  const page = await pageAs(browser);
  await page.goto('/customers');
  await expect(page.getByRole('heading', { name: 'Loyalty guests' })).toBeVisible();

  await page.getByLabel('Give loyalty points on paid bills').check();
  await page.getByLabel('Rupees for 1 point', { exact: true }).fill('100');
  await page.getByLabel('Value of 1 point (₹)', { exact: true }).fill('1');
  await page.getByRole('button', { name: 'Save loyalty settings' }).click();
  await expect(page.getByText('Loyalty settings saved')).toBeVisible();

  await page.getByRole('button', { name: 'Add guest' }).click();
  let dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name', { exact: true }).fill(name);
  await dialog.getByLabel('Mobile number', { exact: true }).fill(phone);
  await dialog.getByRole('button', { name: 'Save' }).click();
  await expect(dialog.getByText('Ask the guest first and tick the box.')).toBeVisible();
  await dialog.getByLabel(/The guest agreed/).check();
  await dialog.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText(`${name} saved`)).toBeVisible();
  await expect(page.getByRole('row', { name: new RegExp(name) })).toBeVisible();

  // The same mobile number written another way is the same guest.
  await page.getByRole('button', { name: 'Add guest' }).click();
  dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name', { exact: true }).fill(`Copy ${id}`);
  await dialog.getByLabel('Mobile number', { exact: true }).fill(`+91 ${phone.slice(0, 5)} ${phone.slice(5)}`);
  await dialog.getByLabel(/The guest agreed/).check();
  await dialog.getByRole('button', { name: 'Save' }).click();
  await expect(dialog.getByText('A guest with this mobile number is already in the list.')).toBeVisible();
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(page.getByRole('row', { name: new RegExp(`Copy ${id}`) })).toHaveCount(0);
});

test('L2 a paid bill earns points and the next bill uses them', async ({ browser }) => {
  const first = await billFor(phone, name);
  const paid = await ok(await api.post(`/api/bills/${first.bill.id}/payments`, { data: { method: 'Cash', amount: first.bill.total, version: first.bill.version } }));
  expect(paid.status).toBe('Paid');
  const earned = Math.floor(Number(first.bill.total) / 100);
  expect(earned).toBeGreaterThan(0);
  expect((await ok(await api.get(`/api/customers/by-phone?phone=${phone}`))).points).toBe(earned);

  const second = await billFor(phone, name);
  const page = await pageAs(browser, cashier.email, cashier.password);
  const dialog = await openBill(page, second.order.tokenNumber);
  await expect(dialog.getByText(`${name}: ${earned} points`)).toBeVisible();
  await dialog.getByRole('button', { name: `Use ${earned} points (₹${earned}.00)` }).click();
  await expect(dialog.getByText(`Loyalty points ₹${earned}.00`)).toBeVisible();
  await expect(dialog.getByText(`${name}: 0 points`)).toBeVisible();
  await expect(dialog.getByRole('button', { name: /^Use / })).toHaveCount(0);
  expect((await ok(await api.get(`/api/customers/by-phone?phone=${phone}`))).points).toBe(0);
});

test('L3 a new mobile number joins from the bill only with consent', async ({ browser }) => {
  const otherPhone = '8' + String(Date.now()).slice(-9);
  const otherName = `Meena ${id}`;
  const { order } = await billFor(otherPhone, otherName);
  const page = await pageAs(browser, cashier.email, cashier.password);
  const dialog = await openBill(page, order.tokenNumber);
  await expect(dialog.getByText('Not a loyalty guest yet.')).toBeVisible();
  await dialog.getByRole('button', { name: 'Add to loyalty' }).click();
  await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue(otherName);
  await dialog.getByRole('button', { name: 'Add to loyalty' }).click();
  await expect(dialog.getByText('Ask the guest first and tick the box.')).toBeVisible();
  await dialog.getByLabel(/The guest agreed/).check();
  await dialog.getByRole('button', { name: 'Add to loyalty' }).click();
  await expect(dialog.getByText(`${otherName}: 0 points`)).toBeVisible();
  expect((await ok(await api.get(`/api/customers/by-phone?phone=${otherPhone}`))).name).toBe(otherName);
});

test('L4 cashier cannot forget a guest; the owner sees the history and forgets', async ({ browser }) => {
  const till = await pageAs(browser, cashier.email, cashier.password);
  await till.goto('/customers');
  await expect(till.getByRole('row', { name: new RegExp(name) })).toBeVisible();
  await expect(till.getByRole('button', { name: `Forget ${name}` })).toHaveCount(0);
  await expect(till.getByRole('heading', { name: 'Loyalty points' })).toHaveCount(0);

  const page = await pageAs(browser);
  await page.goto('/customers');
  await page.getByLabel('Search by name or mobile').fill(id);
  await page.getByRole('button', { name: `Points history ${name}` }).click();
  const history = page.getByRole('dialog');
  await expect(history.getByText('Earned')).toBeVisible();
  await expect(history.getByText('Spent')).toBeVisible();
  await history.getByRole('button', { name: 'Close' }).click();

  await page.getByRole('button', { name: `Forget ${name}` }).click();
  const confirm = page.getByRole('dialog');
  await expect(confirm.getByText(/deleted for good/)).toBeVisible();
  await confirm.getByRole('button', { name: 'Forget', exact: true }).click();
  await expect(page.getByText(`${name} was forgotten`)).toBeVisible();
  const gone = await api.get(`/api/customers/by-phone?phone=${phone}`);
  expect(gone.status()).toBe(404);
});

test('L5 the loyalty API refuses bad input', async () => {
  const noConsent = await api.post('/api/customers', { data: { name: `X ${id}`, phone: '7' + String(Date.now()).slice(-9) } });
  expect(noConsent.status()).toBe(400);
  const badPhone = await api.post('/api/customers', { data: { name: `X ${id}`, phone: '12ab', consent: true } });
  expect(badPhone.status()).toBe(400);
  const badRate = await api.put('/api/customers/settings', { data: { enabled: true, rupeesPerPoint: 0, pointValue: 1 } });
  expect(badRate.status()).toBe(400);
  const missing = await api.delete('/api/customers/999999');
  expect(missing.status()).toBe(404);
});
