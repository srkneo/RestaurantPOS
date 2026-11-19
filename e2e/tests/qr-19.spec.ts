import { test, expect, APIRequestContext, Browser } from '@playwright/test';
import { ok, ownerApi, pageAs, ticket, uid } from './support/pos';

// 1.9 test cases D1-D5: QR ordering at the table (guest page without sign-in).

async function qrTable(api: APIRequestContext, name: string) {
  await ok(await api.put('/api/admin/tables/qr-setting', { data: { enabled: true } }));
  const table = await ok(await api.post('/api/admin/tables', { data: { tableNumber: name, isActive: true, sortOrder: 950 } }));
  const list = await ok(await api.get('/api/admin/tables/qr'));
  const row = list.find((t: { id: number }) => t.id === table.id);
  expect(row, 'table has a QR token').toBeTruthy();
  return { id: table.id as number, token: row.token as string };
}

async function guest(browser: Browser, token: string) {
  const page = await (await browser.newContext()).newPage();
  await page.goto(`/t/${token}`);
  return page;
}

test('D1 the owner sees the QR sheet with the ordering switch on', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  await qrTable(api, `Q${id}`);
  const owner = await pageAs(browser);
  await owner.goto('/admin/tables/qr');
  await expect(owner.getByRole('heading', { name: 'Table QR codes' })).toBeVisible();
  await expect(owner.getByLabel('Guests can order from the QR code')).toBeChecked();
  await expect(owner.getByText(`Q${id}`).first()).toBeVisible();
});

test('D2 a guest orders twice: the order reaches the kitchen and the second is round 2', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  const table = await qrTable(api, `Q${id}`);
  const kitchen = await pageAs(browser);
  await kitchen.goto('/kitchen');

  const page = await guest(browser, table.token);
  await page.getByRole('button', { name: 'Add Fries', exact: true }).click();
  await page.getByRole('button', { name: 'Send order' }).click();
  const sent = page.getByText(/Order sent\. Your token is \d+\./);
  await expect(sent).toBeVisible();
  const token = /token is (\d+)/.exec((await sent.textContent()) ?? '')?.[1] ?? '';
  await expect(ticket(kitchen, token).getByText('1 x Fries')).toBeVisible({ timeout: 10_000 });

  await page.getByRole('button', { name: 'Add Coke', exact: true }).click();
  await page.getByRole('button', { name: 'Send order' }).click();
  await expect(page.getByText(`Order sent. Your token is ${token}.`)).toBeVisible();
  await expect(ticket(kitchen, token).getByText('Round 2')).toBeVisible({ timeout: 10_000 });
});

test('D3 call waiter and ask for bill show up on the POS', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  const table = await qrTable(api, `Q${id}`);
  const pos = await pageAs(browser);
  await pos.goto('/pos');
  await expect(pos.getByRole('button', { name: 'Add Fries', exact: true })).toBeVisible();

  const page = await guest(browser, table.token);
  await page.getByRole('button', { name: 'Call waiter' }).click();
  await expect(pos.getByText(`Table Q${id} is calling a waiter.`)).toBeVisible({ timeout: 10_000 });
  await page.getByRole('button', { name: 'Ask for bill' }).click();
  await expect(pos.getByText(`Table Q${id} asks for the bill.`)).toBeVisible({ timeout: 10_000 });
});

test('D4 after the bill is made the table takes no more QR orders', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  const table = await qrTable(api, `Q${id}`);
  const page = await guest(browser, table.token);
  await page.getByRole('button', { name: 'Add Fries', exact: true }).click();
  await page.getByRole('button', { name: 'Send order' }).click();
  await expect(page.getByText(/Order sent\./)).toBeVisible();

  const board = await ok(await api.get('/api/tables'));
  const row = board.find((t: { id: number }) => t.id === table.id);
  expect(row?.orderId, 'table has an open order').toBeTruthy();
  await ok(await api.post(`/api/orders/${row.orderId}/bill`, { data: {} }));

  await page.getByRole('button', { name: 'Add Coke', exact: true }).click();
  await page.getByRole('button', { name: 'Send order' }).click();
  await expect(page.getByText('The bill for this table is ready. Please ask the staff to add more.')).toBeVisible();
});

test('D5 a new QR code stops the old one', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  const table = await qrTable(api, `Q${id}`);
  const renewed = await ok(await api.post(`/api/admin/tables/${table.id}/qr`));
  expect(renewed.token).not.toBe(table.token);

  const old = await guest(browser, table.token);
  await expect(old.getByText('This QR code is not in use. Please ask the staff.')).toBeVisible();
  const fresh = await guest(browser, renewed.token);
  await expect(fresh.getByRole('button', { name: 'Add Fries', exact: true })).toBeVisible();
});
