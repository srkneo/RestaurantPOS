import { test, expect, Page } from '@playwright/test';
import { addStaff, ownerApi, pageAs, uid } from './support/pos';

// Smoke run over the whole app: each page opens, shows its heading and throws no script error; public and print pages.
const OWNER_PAGES = [
  '/pos', '/orders', '/reservations', '/customers', '/day-close', '/expenses', '/kitchen', '/tokens', '/admin/menu', '/stock',
  '/stock/report', '/admin/tables', '/admin/tables/qr', '/admin/reports', '/reports/day', '/admin/feedback', '/admin/setup',
  '/admin/staff', '/admin/store', '/admin/settings', '/admin/billing', '/admin/licence', '/about', '/help',
];

function watchErrors(page: Page) {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(`${page.url()}: ${e.message}`));
  return errors;
}

// The page finished loading (no "Loading..." left), stayed on its address and shows no error box.
async function opens(page: Page, path: string) {
  await page.goto(path);
  await expect(page.locator('#root'), path).not.toBeEmpty();
  await expect(page.getByText('Loading...', { exact: true }), path).toHaveCount(0, { timeout: 10_000 });
  expect(new URL(page.url()).pathname, path).toBe(path);
  await expect(page.getByRole('button', { name: 'Try again' }), path).toHaveCount(0);
}

test('P1 every page opens without a script error for the owner', async ({ browser }) => {
  const page = await pageAs(browser);
  const errors = watchErrors(page);
  for (const path of OWNER_PAGES) await opens(page, path);
  expect(errors).toEqual([]);
});

test('P2 a cashier sees their pages, and owner pages say no access without a script error', async ({ browser }) => {
  const api = await ownerApi();
  const staff = await addStaff(api, 'cashier', `Smoke ${uid()}`);
  const page = await pageAs(browser, staff.email, staff.password);
  const errors = watchErrors(page);
  for (const path of ['/pos', '/orders', '/reservations', '/customers', '/day-close', '/expenses', '/stock']) await opens(page, path);
  for (const path of ['/admin/staff', '/admin/settings', '/admin/reports', '/reports/day', '/print/day']) {
    await page.goto(path);
    await expect(page.getByText('You do not have access to this page.'), path).toBeVisible();
  }
  expect(errors).toEqual([]);
});

test('P3 public pages open without signing in', async ({ page }) => {
  const errors = watchErrors(page);
  for (const path of ['/terms', '/privacy', '/pricing', '/about', '/help', '/login', '/signup', '/reset-password']) {
    await page.goto(path);
    await expect(page.locator('h1').first(), path).toBeVisible();
    expect(new URL(page.url()).pathname, path).toBe(path);
  }
  expect(errors).toEqual([]);
});

test('P4 print pages: unknown ids show a message, signed-out users go to sign in, unknown pages go home', async ({ browser, page }) => {
  for (const path of ['/print/bill/1', '/print/kot/1', '/print/day', '/print/credit-note/1']) {
    await page.goto(path);
    await expect(page, path).toHaveURL(/\/login/);
  }

  const owner = await pageAs(browser);
  const errors = watchErrors(owner);
  for (const path of ['/print/bill/99999999', '/print/kot/99999999', '/print/credit-note/99999999']) {
    await owner.goto(path);
    await expect(owner.getByRole('button', { name: 'Try again' }), path).toBeVisible({ timeout: 10_000 });
  }
  await owner.goto('/print/day');
  await expect(owner.getByRole('button', { name: 'Try again' })).toHaveCount(0);
  await owner.goto('/no-such-page');
  await expect(owner).not.toHaveURL(/no-such-page/);
  expect(errors).toEqual([]);
});
