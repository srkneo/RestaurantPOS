import { test, expect, APIRequestContext } from '@playwright/test';
import { addCategory, addItem, ok, ownerApi, pageAs, uid } from './support/pos';

// 1.9.1 fixes, checked end to end: ready orders close (Served / Collected, or paying the bill), tables free up,
// the guest QR menu has category buttons and a dish search, only unused tables can be deleted, About lists licences.

test.describe.configure({ mode: 'serial' });

let api: APIRequestContext;
let id: string;
let teaId: number;
let thaliCategory: string;
let thaliName: string;

test.beforeAll(async () => {
  api = await ownerApi();
  id = uid();
  const drinks = await addCategory(api, `Fix Drinks ${id}`);
  thaliCategory = `Fix Thali ${id}`;
  const thali = await addCategory(api, thaliCategory);
  teaId = (await addItem(api, { name: `Fix Tea ${id}`, price: 20, categoryId: drinks.id })).id;
  thaliName = `Fix Plate ${id}`;
  await addItem(api, { name: thaliName, price: 200, categoryId: thali.id });
});

const addTable = async (name: string) => ok(await api.post('/api/admin/tables', { data: { tableNumber: name, isActive: true, sortOrder: 980 } }));
const boardOf = async (tableId: number) => (await ok(await api.get('/api/tables'))).find((t: { id: number }) => t.id === tableId);
const order = async (data: Record<string, unknown>) =>
  ok(await api.post('/api/orders', { data: { items: [{ menuItemId: teaId, quantity: 1 }], clientOrderId: crypto.randomUUID(), ...data } }));
const setStatus = async (orderId: number, status: string) => ok(await api.put(`/api/orders/${orderId}/status`, { data: { status } }));

test('H1 Served on Orders keeps a dine-in table busy until the bill is paid', async ({ browser }) => {
  const table = await addTable(`S${id}`);
  const placed = await order({ orderType: 'DineIn', table: table.id });
  await setStatus(placed.orderId, 'Ready');
  expect((await boardOf(table.id)).status).toBe('Occupied');

  const page = await pageAs(browser);
  await page.goto('/orders');
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: String(placed.tokenNumber), exact: true }) });
  await row.getByRole('button', { name: 'Food delivered' }).click();
  await expect(page.getByText(`Token ${placed.tokenNumber}: food delivered. The table stays busy until the bill is paid.`)).toBeVisible();
  expect((await ok(await api.get(`/api/orders/${placed.orderId}`))).status).toBe('Served');
  expect((await boardOf(table.id)).status).toBe('Occupied');

  const bill = await ok(await api.post(`/api/orders/${placed.orderId}/bill`, { data: {} }));
  await ok(await api.post(`/api/bills/${bill.id}/payments`, { data: { method: 'cash', amount: Number(bill.total), version: bill.version } }));
  await expect.poll(async () => (await boardOf(table.id)).status).toBe('Paid');
  expect((await ok(await api.get(`/api/orders/${placed.orderId}`))).status).toBe('Completed');

  // The guests go: Customer left on the table grid frees the table.
  await page.goto('/pos');
  await page.getByRole('button', { name: 'Dine-in', exact: true }).click();
  await page.locator('#pos-table').click();
  const grid = page.getByRole('dialog', { name: 'Tables' });
  await grid.getByRole('button', { name: `Customer left table S${id}`, exact: true }).click();
  await expect(page.getByText(`Table S${id} is free again.`)).toBeVisible();
  await expect.poll(async () => (await boardOf(table.id)).status).toBe('Free');
});

test('H2 paying the bill of a ready takeaway closes it and it leaves the token screen', async ({ browser }) => {
  const placed = await order({ orderType: 'Takeaway', customerName: `Pay ${id}` });
  await setStatus(placed.orderId, 'Ready');

  const tokens = await pageAs(browser);
  await tokens.goto('/tokens');
  const ready = tokens.getByRole('region', { name: 'Ready to collect' });
  await expect(ready.getByText(String(placed.tokenNumber), { exact: true })).toBeVisible();

  const bill = await ok(await api.post(`/api/orders/${placed.orderId}/bill`, { data: {} }));
  const paid = await ok(await api.post(`/api/bills/${bill.id}/payments`, { data: { method: 'cash', amount: Number(bill.total), version: bill.version } }));
  expect(paid.status).toBe('Paid');
  expect((await ok(await api.get(`/api/orders/${placed.orderId}`))).status).toBe('Completed');
  await expect(ready.getByText(String(placed.tokenNumber), { exact: true })).toHaveCount(0);
});

test('H3 a takeaway that is ready shows Collected on Orders', async ({ browser }) => {
  const placed = await order({ orderType: 'Takeaway', customerName: `Collect ${id}` });
  await setStatus(placed.orderId, 'Ready');
  const page = await pageAs(browser);
  await page.goto('/orders');
  const row = page.getByRole('row').filter({ hasText: `Collect ${id}` });
  await row.getByRole('button', { name: 'Handed over' }).click();
  await expect(page.getByText(`Token ${placed.tokenNumber} done.`)).toBeVisible();
  expect((await ok(await api.get(`/api/orders/${placed.orderId}`))).status).toBe('Completed');
});

test('H4 the guest QR menu has category buttons and a dish search', async ({ browser }) => {
  await ok(await api.put('/api/admin/tables/qr-setting', { data: { enabled: true } }));
  const table = await addTable(`Q${id}`);
  const qr = (await ok(await api.get('/api/admin/tables/qr'))).find((t: { id: number }) => t.id === table.id);
  const guest = await (await browser.newContext({ viewport: { width: 390, height: 844 } })).newPage();
  await guest.goto(`/t/${qr.token}`);

  const sections = guest.getByRole('navigation', { name: 'Menu sections' });
  await sections.getByRole('button', { name: thaliCategory, exact: true }).click();
  await expect(guest.getByRole('heading', { name: thaliCategory, exact: true })).toBeInViewport();

  await guest.getByLabel('Search dishes').fill(`Plate ${id}`);
  await expect(guest.getByRole('button', { name: `Add ${thaliName}`, exact: true })).toBeVisible();
  await expect(guest.getByRole('button', { name: `Add Fix Tea ${id}`, exact: true })).toHaveCount(0);
  await guest.getByLabel('Search dishes').fill('zzzz-no-such-dish');
  await expect(guest.getByText('No dish matches. Try another word.')).toBeVisible();
});

test('H5 only a table that never had an order can be deleted', async ({ browser }) => {
  const spare = await addTable(`D${id}`);
  const used = await addTable(`U${id}`);
  const placed = await order({ orderType: 'DineIn', table: used.id });
  await setStatus(placed.orderId, 'Ready');
  await setStatus(placed.orderId, 'Completed');
  expect(spare.id).toBeGreaterThan(0);

  const page = await pageAs(browser);
  await page.goto('/admin/tables');
  const usedRow = page.getByRole('row').filter({ hasText: `U${id}` });
  await expect(usedRow.getByRole('button', { name: 'Edit' })).toBeVisible();
  await expect(usedRow.getByRole('button', { name: 'Delete' })).toHaveCount(0);

  const spareRow = page.getByRole('row').filter({ hasText: `D${id}` });
  await spareRow.getByRole('button', { name: 'Delete' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByText(`Table D${id} deleted.`)).toBeVisible();
  await expect(spareRow).toHaveCount(0);
  expect((await api.delete(`/api/admin/tables/${used.id}`)).status()).toBe(409);
});

test('H6 About lists the open-source libraries and their licences', async ({ browser }) => {
  const page = await pageAs(browser);
  await page.goto('/about');
  await page.getByText('Open-source software').click();
  await expect(page.getByRole('link', { name: 'react', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'QRCoder', exact: true })).toBeVisible();
});
