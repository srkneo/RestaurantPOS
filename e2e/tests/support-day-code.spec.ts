import { test, expect, request } from '@playwright/test';
import { BASE_URL, ownerApi } from './support/pos';

// SUP2: support signs in on a shop PC with today's code from the platform. The E2E server has no shop lease,
// so a code is always refused here; the full sign-in is covered by SupportDayLoginTests (xUnit).

test('SD1 the sign-in page links to the support day code page', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('link', { name: "Support: sign in with today's code" }).click();
  await expect(page).toHaveURL(/\/support-day-login/);
  await expect(page.getByRole('heading', { name: 'Support sign-in' })).toBeVisible();
});

test('SD2 an empty code is not sent', async ({ page }) => {
  await page.goto('/support-day-login');
  await page.getByRole('button', { name: 'Sign in as support' }).click();
  await expect(page.getByText('Enter the code from support.')).toBeVisible();
  await expect(page).toHaveURL(/\/support-day-login/);
});

test('SD3 a wrong code shows an error and does not sign in', async ({ page }) => {
  await page.goto('/support-day-login');
  await page.getByLabel("Today's code").fill('ABCD-EFGH');
  await page.getByRole('button', { name: 'Sign in as support' }).click();
  await expect(page.getByRole('alert')).toContainText(/fresh licence|wrong or not for today/);
  await expect(page).toHaveURL(/\/support-day-login/);
});

test('SD4 the API refuses bad input, and only the platform gets a code', async () => {
  const anon = await request.newContext({ baseURL: BASE_URL });
  expect((await anon.post('/api/auth/support-day-login', { data: {} })).status()).toBe(400);
  expect((await anon.post('/api/auth/support-day-login', { data: { code: 'x'.repeat(500) } })).status()).toBe(400);
  expect([401, 403, 302]).toContain((await anon.get('/api/platform/licences/1/support-code', { maxRedirects: 0 })).status());
  const owner = await ownerApi();
  expect((await owner.get('/api/platform/licences/1/support-code', { maxRedirects: 0 })).status()).toBe(403);
});
