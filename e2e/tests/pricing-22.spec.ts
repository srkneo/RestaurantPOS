import { test, expect } from '@playwright/test';

// v2.0: public pricing page, reached from the sign-in page, leads to the free trial.
test('P1 the sign-in page links to the pricing page, which shows both plans and starts the trial', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('link', { name: 'See prices and features' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Simple pricing, no hidden charges' })).toBeVisible();
  await expect(page.getByText(/ a month$/)).toBeVisible();
  await expect(page.getByText(/ a year$/)).toBeVisible();
  await page.getByRole('link', { name: 'Start free trial' }).click();
  await expect(page.getByRole('heading', { name: 'Start your free trial' })).toBeVisible();
});
