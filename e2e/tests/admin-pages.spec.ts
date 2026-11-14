import { test, expect, Page } from '@playwright/test';

const email = process.env.E2E_EMAIL ?? 'admin@pos.local';
const password = process.env.E2E_PASSWORD ?? 'ChangeMe#12345';

async function signIn(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/login'));
}

test('owner names the store and adds a table the POS can use', async ({ page }) => {
  await signIn(page);
  const suffix = Date.now() % 100000;

  await page.getByRole('link', { name: 'Store', exact: true }).click();
  const name = `E2E Dining ${suffix}`;
  await page.getByLabel('Restaurant name').fill(name);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('Store profile saved.')).toBeVisible();
  await page.reload();
  await expect(page.locator('.pos-store-name')).toHaveText(name);

  const table = `E2E ${suffix}`;
  await page.getByRole('link', { name: 'Tables', exact: true }).click();
  await page.getByRole('button', { name: 'Add table' }).click();
  await page.getByRole('dialog').getByLabel('Table number or name').fill(table);
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('cell', { name: table, exact: true })).toBeVisible();

  await page.getByRole('link', { name: 'POS', exact: true }).click();
  await expect(page.getByLabel('Table', { exact: true }).locator('option', { hasText: table })).toHaveCount(1);
});
