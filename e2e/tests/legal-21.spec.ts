import { test, expect } from '@playwright/test';

// v2.0: draft terms of service and privacy policy, open to everyone and linked from the signup page.
test('L1 signup links to the terms and privacy pages, which open without signing in', async ({ page }) => {
  await page.goto('/signup');
  await expect(page.getByRole('link', { name: 'Terms of service' })).toHaveAttribute('href', '/terms');
  await expect(page.getByRole('link', { name: 'Privacy policy' })).toHaveAttribute('href', '/privacy');

  await page.goto('/terms');
  await expect(page.getByRole('heading', { level: 1, name: 'Terms of service' })).toBeVisible();
  await expect(page.getByRole('note')).toContainText('Draft for review');

  await page.goto('/privacy');
  await expect(page.getByRole('heading', { level: 1, name: 'Privacy policy' })).toBeVisible();
});
