import { test, expect, request } from '@playwright/test';
import { BASE_URL, ownerApi, addCategory, addItem, addStaff, signIn, uid } from './support/pos';

// UI1: the owner's Activity log (Admin -> Activity log). The E2E server has no platform account, so the
// support sign-in rows and the platform view per restaurant are covered by ActivityLogTests (xUnit).

test('AL1 the owner sees a new dish in the activity log and can filter by type', async ({ page }) => {
  const id = uid();
  const api = await ownerApi();
  const category = await addCategory(api, `Activity ${id}`);
  await addItem(api, { name: `Activity Samosa ${id}`, price: 20, categoryId: category.id });

  await signIn(page);
  await page.getByRole('button', { name: 'Admin', exact: true }).click();
  await page.getByRole('link', { name: 'Activity log', exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/activity/);
  await expect(page.getByRole('heading', { name: 'Activity log' })).toBeVisible();
  const row = page.getByRole('row').filter({ hasText: `Activity Samosa ${id}` });
  await expect(row).toHaveCount(1);
  await expect(row).toContainText('Created · Menu item');

  await page.getByLabel('Type').selectOption('MenuCategory');
  await expect(page.getByRole('row').filter({ hasText: `Activity Samosa ${id}` })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: `Activity ${id}` }).first()).toContainText('Menu category');
});

test('AL2 a manager has no activity log: no menu link, the page is refused and the API says 403', async ({ browser }) => {
  const api = await ownerApi();
  const manager = await addStaff(api, 'manager', `Activity Manager ${uid()}`);

  const page = await (await browser.newContext()).newPage();
  await signIn(page, manager.email, manager.password);
  await expect(page.getByRole('link', { name: 'Activity log', exact: true })).toHaveCount(0);
  await page.goto('/admin/activity');
  await expect(page.getByText('You do not have access to this page.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Activity log' })).toHaveCount(0);

  const managerApi = await request.newContext({ baseURL: BASE_URL });
  expect((await managerApi.post('/api/auth/login', { data: { email: manager.email, password: manager.password } })).ok()).toBeTruthy();
  expect((await managerApi.get('/api/admin/audit')).status()).toBe(403);
  expect((await managerApi.get('/api/platform/tenants/1/audit')).status()).toBe(403);
});

test('AL3 the API refuses strangers and bad input', async () => {
  const anon = await request.newContext({ baseURL: BASE_URL });
  expect([401, 403, 302]).toContain((await anon.get('/api/admin/audit', { maxRedirects: 0 })).status());
  const owner = await ownerApi();
  expect((await owner.get('/api/admin/audit?take=abc')).status()).toBe(400);
  expect((await owner.get('/api/admin/audit?type=NoSuchType')).status()).toBe(200);
  expect(await (await owner.get('/api/admin/audit?type=NoSuchType')).json()).toEqual([]);
  expect((await owner.get('/api/platform/tenants/1/audit')).status()).toBe(403);
});
