import { test, expect, Page, APIRequestContext } from '@playwright/test';
import { addStaff, ok, ownerApi, pageAs, placedToken, placeTakeaway, ticket, uid } from './support/pos';

// 1.9 test cases C1-C5: table grid, running tab, move / merge / split, captain (waiter) mode, token screen.

async function addTable(api: APIRequestContext, name: string) {
  return ok(await api.post('/api/admin/tables', { data: { tableNumber: name, isActive: true, sortOrder: 900 } }));
}

async function pickTable(page: Page, name: string) {
  await page.getByLabel('Table', { exact: true }).click();
  await page.getByRole('dialog', { name: 'Tables' }).getByRole('button', { name: `Table ${name}`, exact: true }).click();
}

async function dineIn(page: Page, table: string, items: string[]) {
  await page.goto('/pos');
  await pickTable(page, table);
  for (const name of items) await page.getByRole('button', { name: `Add ${name}`, exact: true }).click();
  await page.getByRole('button', { name: 'Place order' }).click();
}

const orderRow = (page: Page, token: string) =>
  page.getByRole('row').filter({ has: page.getByRole('cell', { name: token, exact: true }) });

test('C1 table grid: busy tables show the token, free tables say Free', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  await addTable(api, `G${id}`);
  await addTable(api, `H${id}`);
  const pos = await pageAs(browser);
  await dineIn(pos, `G${id}`, ['Fries']);
  const token = await placedToken(pos);

  await pos.getByLabel('Table', { exact: true }).click();
  const grid = pos.getByRole('dialog', { name: 'Tables' });
  await expect(grid.getByRole('button', { name: `Table G${id}`, exact: true })).toContainText(`Token ${token}`);
  await expect(grid.getByRole('button', { name: `Table H${id}`, exact: true })).toContainText('Free');
  await grid.getByRole('button', { name: `Table H${id}`, exact: true }).click();
  await expect(grid).toHaveCount(0);
  await expect(pos.getByLabel('Table', { exact: true })).toContainText(`Table H${id}`);
});

test('C2 running tab: a second order on a busy table is round 2 of the same token', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  await addTable(api, `R${id}`);
  const kitchen = await pageAs(browser);
  await kitchen.goto('/kitchen');
  const pos = await pageAs(browser);
  await dineIn(pos, `R${id}`, ['Fries']);
  const token = await placedToken(pos);

  await pos.goto('/pos');
  await pickTable(pos, `R${id}`);
  await expect(pos.getByText(`Table R${id} has token ${token} open. New items are added to it.`)).toBeVisible();
  await pos.getByRole('button', { name: 'Add Coke', exact: true }).click();
  await pos.getByRole('button', { name: `Add to token ${token}` }).click();
  await expect(pos.getByText(`Added to token ${token}.`)).toBeVisible();

  const t = ticket(kitchen, token);
  await expect(t.getByText('1 x Coke')).toBeVisible({ timeout: 10_000 });
  await expect(t.getByText('Round 2')).toBeVisible();
});

test('C3 move, merge and split from the Orders page', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  for (const n of ['M1', 'M2', 'M3']) await addTable(api, `${n}${id}`);
  const pos = await pageAs(browser);
  await dineIn(pos, `M1${id}`, ['Fries']);
  const first = await placedToken(pos);
  await dineIn(pos, `M2${id}`, ['Coke', 'Coke']);
  const second = await placedToken(pos);

  await pos.goto('/orders');
  // Move: M1 -> M3
  await orderRow(pos, first).getByRole('button', { name: 'Move table' }).click();
  let dialog = pos.getByRole('dialog');
  await dialog.getByLabel('Move to').selectOption({ label: `Table M3${id}` });
  await dialog.getByRole('button', { name: 'Move', exact: true }).click();
  await expect(pos.getByText(`Token ${first} moved to Table M3${id}.`)).toBeVisible();

  // Merge: the first order (now on M3) into the second (M2)
  await orderRow(pos, second).getByRole('button', { name: 'Merge table' }).click();
  dialog = pos.getByRole('dialog');
  await dialog.getByLabel('Merge in').selectOption({ label: `Token ${first} · Table M3${id}` });
  await dialog.getByRole('button', { name: 'Merge', exact: true }).click();
  await expect(pos.getByText(`Token ${first} merged into token ${second}.`)).toBeVisible();
  await expect(orderRow(pos, second)).toContainText('1 x Fries');

  // Split: one Coke to a bill of its own
  await orderRow(pos, second).getByRole('button', { name: 'Split bill' }).click();
  dialog = pos.getByRole('dialog');
  await dialog.getByLabel('2 x Coke').fill('1');
  await dialog.getByRole('button', { name: 'Split', exact: true }).click();
  await expect(pos.getByText(/Split off as token \d+\. Bill it from this list\./)).toBeVisible();
});

test('C4 captain (waiter) takes orders but cannot bill', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  await addTable(api, `W${id}`);
  const waiter = await addStaff(api, 'waiter', `Suresh ${id}`);
  const page = await pageAs(browser, waiter.email, waiter.password);
  await expect(page).toHaveURL(/\/pos/);
  await dineIn(page, `W${id}`, ['Fries']);
  await placedToken(page);
  await expect(page.getByRole('button', { name: 'Bill & pay' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Orders', exact: true })).toHaveCount(0);

  const res = await page.request.get('/api/bills');
  expect([401, 403]).toContain(res.status());
});

test('C5 token screen: a token moves from Preparing to Ready live', async ({ browser }) => {
  const pos = await pageAs(browser);
  const id = uid();
  const token = await placeTakeaway(pos, `Token ${id}`, ['Fries']);

  const screenPage = await pageAs(browser);
  await screenPage.goto('/tokens');
  const preparing = screenPage.getByRole('region', { name: 'Preparing' });
  const ready = screenPage.getByRole('region', { name: 'Ready to collect' });
  await expect(preparing.getByText(token, { exact: true })).toBeVisible({ timeout: 10_000 });

  const kitchen = await pageAs(browser);
  await kitchen.goto('/kitchen');
  await ticket(kitchen, token).getByRole('button', { name: 'Mark ready' }).click();
  await expect(ready.getByText(token, { exact: true })).toBeVisible({ timeout: 10_000 });
  await expect(preparing.getByText(token, { exact: true })).toHaveCount(0);
});
