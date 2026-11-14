import { test, expect, Page } from '@playwright/test';

const email = process.env.E2E_EMAIL ?? 'admin@pos.local';
const password = process.env.E2E_PASSWORD ?? 'ChangeMe#12345';

async function signIn(page: Page, path: string) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/login'));
  await page.goto(path);
}

test('order flows from POS to kitchen and back', async ({ browser }) => {
  const kitchen = await (await browser.newContext()).newPage();
  const pos = await (await browser.newContext()).newPage();

  await signIn(kitchen, '/kitchen');
  await expect(kitchen.getByText('Kitchen Display - Live Orders')).toBeVisible();

  await signIn(pos, '/pos');
  await pos.locator('select').selectOption({ label: 'Table 1' });
  await pos.getByRole('heading', { name: 'Classic Burger' }).click();
  await pos.getByRole('heading', { name: 'Classic Burger' }).click();
  await pos.getByRole('heading', { name: 'Fries', exact: true }).click();

  const dialogPromise = pos.waitForEvent('dialog');
  await pos.getByRole('button', { name: 'Place Order' }).click();
  const dialog = await dialogPromise;
  const token = /Token Number: (\d+)/.exec(dialog.message())?.[1];
  await dialog.accept();
  expect(token).toBeTruthy();

  const ticketTitle = kitchen.getByText(new RegExp(`Token #${token}$`));
  await expect(ticketTitle).toBeVisible({ timeout: 10_000 });
  await expect(kitchen.getByText('2 x Classic Burger').first()).toBeVisible();

  await ticketTitle
    .locator('xpath=ancestor::div[.//button[normalize-space()="Mark Ready"]][1]')
    .getByRole('button', { name: 'Mark Ready' })
    .click();

  await expect(pos.getByText(`Order ${token} is Ready`)).toBeVisible({ timeout: 10_000 });
  await expect(ticketTitle).toHaveCount(0);
});
