import { test, expect } from '@playwright/test';

// v2.4 RN1: the app is called AllInOne POS on the sign-in page, in the browser tab and in the install manifest.
test('RN1 sign-in page and tab title show AllInOne POS', async ({ page }) => {
  await page.goto('/login');
  await expect(page).toHaveTitle('AllInOne POS');
  await expect(page.locator('.pos-brand')).toHaveText('AllInOne POS');
  await expect(page.getByText('Restaurant POS')).toHaveCount(0);
});

test('RN1 install manifest names the app', async ({ request }) => {
  const res = await request.get('/manifest.webmanifest');
  expect(res.ok()).toBeTruthy();
  const text = await res.text();
  const manifest = JSON.parse(text);
  expect(manifest.name).toBe('AllInOne POS');
  expect(manifest.short_name).toBe('AllInOne POS');
  expect(text).not.toContain('Restaurant POS');
});

test('RN1 negative: a wrong password shows the error and the name stays AllInOne POS', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill('nobody@pos.local');
  await page.getByLabel('Password').fill('wrong-password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toContainText('Invalid email or password');
  await expect(page.locator('.pos-brand')).toHaveText('AllInOne POS');
  await expect(page).toHaveTitle('AllInOne POS');
});
