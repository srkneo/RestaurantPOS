import { test, expect } from '@playwright/test';
import { uid } from './support/pos';

// v2.0 self-signup: a new restaurant starts a 30-day trial from the sign-in page and lands on the setup checklist.
test('S1 a new restaurant signs up for a 30-day trial and lands on the setup checklist', async ({ page }) => {
  const id = uid();
  await page.goto('/login');
  await page.getByRole('link', { name: 'New business? Start a 30-day free trial' }).click();
  await expect(page.getByRole('heading', { name: 'Start your free trial' })).toBeVisible();
  await expect(page.getByText(/30 days free/)).toBeVisible();

  await page.getByLabel('Business name').fill(`Trial Cafe ${id}`);
  await page.getByLabel('Your name').fill('Kiran Patel');
  await page.getByLabel('Email (you sign in with it)').fill(`trial-${id}@test.local`);
  await page.getByLabel('Mobile number').fill('9825012345');
  await page.getByLabel('Password', { exact: true }).fill('Owner#2026xy');
  await page.getByRole('button', { name: 'Start free trial' }).click();
  await page.waitForURL('**/admin/setup');

  const sub = await (await page.request.get('/api/subscription')).json();
  expect(sub.state).toBe('trial');
  expect(sub.daysLeft).toBe(30);
  expect(await (await page.request.get('/api/tables')).json()).toHaveLength(10);
});
