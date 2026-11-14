import { test, expect, Page } from '@playwright/test';

const ownerEmail = process.env.E2E_EMAIL ?? 'admin@pos.local';
const ownerPassword = process.env.E2E_PASSWORD ?? 'ChangeMe#12345';

async function signIn(page: Page, email: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

test('owner adds a cashier who can sign in, then deactivates them', async ({ browser }) => {
  const staffEmail = `cashier-${Date.now()}@test.local`;
  const staffPassword = 'Cashier#2026x';

  const owner = await (await browser.newContext()).newPage();
  await signIn(owner, ownerEmail, ownerPassword);
  await owner.waitForURL((url) => !url.pathname.startsWith('/login'));
  await owner.getByRole('link', { name: 'Staff', exact: true }).click();
  await expect(owner.getByRole('heading', { name: 'Staff', exact: true })).toBeVisible();

  await owner.getByRole('button', { name: 'Add staff' }).click();
  const add = owner.getByRole('dialog');
  await add.getByLabel('Name', { exact: true }).fill('Test Cashier');
  await add.getByLabel('Email', { exact: true }).fill(staffEmail);
  await add.getByLabel('Role', { exact: true }).selectOption('cashier');
  await add.getByLabel('Password', { exact: true }).fill(staffPassword);
  await add.getByRole('button', { name: 'Save' }).click();

  const row = owner.getByRole('row').filter({ hasText: staffEmail });
  await expect(row).toBeVisible();

  const cashier = await (await browser.newContext()).newPage();
  await signIn(cashier, staffEmail, staffPassword);
  await expect(cashier).toHaveURL(/\/pos$/);
  await expect(cashier.getByRole('link', { name: 'Staff', exact: true })).toHaveCount(0);

  await row.getByRole('button', { name: 'Edit' }).click();
  const edit = owner.getByRole('dialog');
  await edit.getByLabel('Active', { exact: true }).uncheck();
  await edit.getByRole('button', { name: 'Save' }).click();
  await expect(row.getByText('Inactive', { exact: true })).toBeVisible();

  const again = await (await browser.newContext()).newPage();
  await signIn(again, staffEmail, staffPassword);
  await expect(again.getByRole('alert')).toHaveText('Invalid email or password');
});
