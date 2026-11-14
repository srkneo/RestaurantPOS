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
  // First page of the run: the app may still be warming up.
  await expect(kitchen.getByRole('heading', { name: 'Kitchen', exact: true })).toBeVisible({ timeout: 15_000 });

  await signIn(pos, '/pos');
  await pos.getByLabel('Table', { exact: true }).selectOption({ label: 'Table 1' });
  await pos.getByRole('button', { name: 'Add Classic Burger' }).click();
  await pos.getByRole('button', { name: 'Add Classic Burger' }).click();
  await pos.getByRole('button', { name: 'Add Fries' }).click();
  await pos.getByRole('button', { name: 'Place order' }).click();

  const placed = pos.getByText(/Order placed\. Token \d+/);
  await expect(placed).toBeVisible();
  const token = /Token (\d+)/.exec((await placed.textContent()) ?? '')?.[1];
  expect(token).toBeTruthy();

  const ticket = kitchen.getByRole('article', { name: `Token ${token}`, exact: true });
  await expect(ticket).toBeVisible({ timeout: 10_000 });
  await expect(ticket.getByText('2 x Classic Burger')).toBeVisible();

  await ticket.getByRole('button', { name: 'Mark ready' }).click();

  await expect(pos.getByText(`Order ${token} is ready`)).toBeVisible({ timeout: 10_000 });
  await expect(ticket).toHaveCount(0);
});
