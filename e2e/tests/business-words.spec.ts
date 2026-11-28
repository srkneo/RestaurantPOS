import { test, expect, request } from '@playwright/test';
import { BASE_URL, ownerApi, signIn } from './support/pos';

// v2.4 RN5: users see "business", not "restaurant". The E2E server has no platform account, so the
// platform list (Businesses, Add business) is covered by PlatformPage.test.jsx and BusinessWordsTests (xUnit).

test('BW1 the sign-in and signup pages say business', async ({ page }) => {
  await page.goto('/login');
  await expect(page.getByRole('link', { name: 'New restaurant? Start a 30-day free trial' })).toHaveCount(0);
  await page.getByRole('link', { name: 'New business? Start a 30-day free trial' }).click();
  await expect(page.getByRole('heading', { name: 'Start your free trial' })).toBeVisible();
  await expect(page.getByLabel('Business name')).toBeVisible();
  await expect(page.getByLabel('Restaurant name')).toHaveCount(0);
});

test('BW2 the owner Store page asks for the business name', async ({ page }) => {
  await signIn(page);
  await page.getByRole('button', { name: 'Admin', exact: true }).click();
  await page.getByRole('link', { name: 'Store', exact: true }).click();
  await expect(page.getByLabel('Business name')).toBeVisible();
  await expect(page.getByLabel('Restaurant name')).toHaveCount(0);
});

test('BW3 a blank business name is refused with the business wording', async () => {
  const api = await ownerApi();
  const store = await api.put('/api/admin/store', { data: { restaurantName: ' ', outletName: 'Main' } });
  expect(store.status()).toBe(400);
  const storeText = await store.text();
  expect(storeText).toContain('Enter the business name (up to 200 characters).');
  expect(storeText.toLowerCase()).not.toContain('restaurant name');

  const anon = await request.newContext({ baseURL: BASE_URL });
  const signup = await anon.post('/api/signup', { data: { restaurantName: '', ownerName: 'Kiran Patel', ownerEmail: 'blank-name@test.local', ownerPhone: '9825012345', ownerPassword: 'Owner#2026xy' } });
  expect(signup.status()).toBe(400);
  const signupText = await signup.text();
  expect(signupText).toContain('Enter the business name.');
  expect(signupText.toLowerCase()).not.toContain('restaurant name');
});
