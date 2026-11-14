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

async function placedToken(page: Page) {
  const placed = page.getByText(/Order placed\. Token \d+/).last();
  await expect(placed).toBeVisible();
  return /Token (\d+)/.exec((await placed.textContent()) ?? '')?.[1] ?? '';
}

test('takeaway order shows the customer name in the kitchen and can be cancelled', async ({ browser }) => {
  const pos = await (await browser.newContext()).newPage();
  const kitchen = await (await browser.newContext()).newPage();
  await signIn(pos);
  await signIn(kitchen);
  await kitchen.goto('/kitchen');

  await pos.goto('/pos');
  await pos.getByRole('button', { name: 'Takeaway', exact: true }).click();
  await pos.getByLabel('Customer name').fill('Ravi');
  await pos.getByRole('button', { name: 'Add Fries' }).click();
  await pos.getByRole('button', { name: 'Add note for Fries' }).click();
  await pos.getByRole('textbox', { name: 'Add note for Fries' }).fill('Jain');
  await pos.getByRole('button', { name: 'Place order' }).click();
  const token = await placedToken(pos);

  const ticket = kitchen.getByRole('article', { name: `Token ${token}`, exact: true });
  await expect(ticket).toBeVisible({ timeout: 10_000 });
  await expect(ticket.getByText('Takeaway · Ravi')).toBeVisible();
  await expect(ticket.getByText('Jain')).toBeVisible();

  await pos.goto('/orders');
  const row = pos.getByRole('row').filter({ hasText: 'Takeaway · Ravi' }).filter({ has: pos.getByRole('cell', { name: token, exact: true }) });
  await row.getByRole('button', { name: 'Cancel order' }).click();
  const dialog = pos.getByRole('dialog');
  await dialog.getByLabel('Reason').selectOption({ label: 'Customer left' });
  await dialog.getByRole('button', { name: 'Yes, cancel' }).click();
  await expect(row.getByText('Cancelled', { exact: true })).toBeVisible();
  await expect(ticket).toHaveCount(0, { timeout: 10_000 });
});

test('an order can be changed before cooking, not after', async ({ page }) => {
  await signIn(page);
  await page.goto('/pos');
  await page.getByLabel('Table', { exact: true }).selectOption({ label: 'Table 2' });
  await page.getByRole('button', { name: 'Add Fries' }).click();
  await page.getByRole('button', { name: 'Place order' }).click();
  const token = await placedToken(page);

  await page.goto('/orders');
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: token, exact: true }) });
  await row.getByRole('link', { name: 'Edit' }).click();
  await expect(page.getByText(`Editing token ${token}`)).toBeVisible();
  await page.getByRole('button', { name: 'One more Fries' }).click();
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText(`Token ${token} updated.`)).toBeVisible();
  await expect(row.getByText('2 x Fries')).toBeVisible();

  await page.goto('/kitchen');
  const ticket = page.getByRole('article', { name: `Token ${token}`, exact: true });
  await ticket.getByRole('button', { name: 'Start cooking' }).click();
  await expect(ticket.getByText('Cooking', { exact: true })).toBeVisible();

  await page.goto('/orders');
  await expect(row.getByRole('link', { name: 'Edit' })).toHaveCount(0);
  await expect(row.getByText('Preparing', { exact: true })).toBeVisible();
});

test('a sold-out item disappears from the POS', async ({ page }) => {
  await signIn(page);
  await page.getByRole('link', { name: 'Menu', exact: true }).click();
  await page.getByRole('switch', { name: 'Coke: Available' }).click();
  await expect(page.getByText('Coke: Sold out')).toBeVisible();

  await page.goto('/pos');
  await expect(page.getByRole('button', { name: 'Add Fries' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add Coke' })).toHaveCount(0);

  await page.goto('/admin/menu');
  await page.getByRole('switch', { name: 'Coke: Available' }).click();
  await expect(page.getByText('Coke: Available')).toBeVisible();
});
