import { test, expect, request, APIRequestContext } from '@playwright/test';
import { BASE_URL, OWNER_EMAIL, OWNER_PASSWORD, STAFF_PASSWORD, addStaff, ownerApi, pageAs, signIn, uid } from './support/pos';

// Sign in, sign out, lockout, who may open which page, and bad input on signup and owner reset.
test.describe.configure({ mode: 'serial' });

let api: APIRequestContext;

test.beforeAll(async () => {
  api = await ownerApi();
});

async function anonymous() {
  return request.newContext({ baseURL: BASE_URL });
}

async function loginStatus(ctx: APIRequestContext, data: unknown) {
  return (await ctx.post('/api/auth/login', { data })).status();
}

// Wrong passwords are tried on fresh staff accounts, never the owner, so the owner is never locked out.
test('A1 a wrong password shows an error and stays on the sign-in page', async ({ page }) => {
  const staff = await addStaff(api, 'cashier', `Typo ${uid()}`);
  await page.goto('/login');
  await page.getByLabel('Email').fill(staff.email);
  await page.getByLabel('Password').fill('Wrong#password1');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByText('Invalid email or password')).toBeVisible();
  await expect(page).toHaveURL(/\/login/);
});

test('A2 the login API refuses bad input without a server error', async () => {
  const staff = await addStaff(api, 'cashier', `Long ${uid()}`);
  const anon = await anonymous();
  expect(await loginStatus(anon, { email: `nobody-${uid()}@test.local`, password: 'Whatever#123' })).toBe(401);
  expect(await loginStatus(anon, { email: "' OR 1=1 --", password: "' OR 1=1 --" })).toBe(401);
  expect(await loginStatus(anon, { email: staff.email, password: 'x'.repeat(5000) })).toBe(401);
  expect(await loginStatus(anon, {})).toBe(401);
  expect(await loginStatus(anon, { email: '', password: '' })).toBe(401);
  const broken = await anon.post('/api/auth/login', { headers: { 'Content-Type': 'application/json' }, data: '{ not json' });
  expect(broken.status()).toBeGreaterThanOrEqual(400);
  expect(broken.status()).toBeLessThan(500);
  await anon.dispose();
});

test('A3 five wrong passwords lock the account', async () => {
  const staff = await addStaff(api, 'cashier', `Lock ${uid()}`);
  const anon = await anonymous();
  expect(await loginStatus(anon, { email: staff.email, password: STAFF_PASSWORD })).toBe(200);
  const fresh = await anonymous();
  for (let i = 0; i < 5; i++) expect(await loginStatus(fresh, { email: staff.email, password: 'Wrong#pass1' })).toBe(401);
  expect(await loginStatus(fresh, { email: staff.email, password: STAFF_PASSWORD })).toBe(401);
  await anon.dispose();
  await fresh.dispose();
});

test('A4 without signing in, pages go to sign-in and the API says 401', async ({ page }) => {
  for (const path of ['/orders', '/pos', '/admin/staff', '/customers', '/stock']) {
    await page.goto(path);
    await expect(page).toHaveURL(/\/login/);
  }
  const anon = await anonymous();
  for (const path of ['/api/orders', '/api/customers', '/api/staff', '/api/reservations', '/api/stock']) {
    expect((await anon.get(path)).status(), path).toBe(401);
  }
  await anon.dispose();
});

test('A5 a cashier cannot open owner pages or call owner APIs', async ({ browser }) => {
  const staff = await addStaff(api, 'cashier', `Till ${uid()}`);
  const page = await pageAs(browser, staff.email, staff.password);
  for (const path of ['/admin/staff', '/admin/settings', '/admin/billing', '/admin/menu', '/admin/reports']) {
    await page.goto(path);
    await expect(page.getByText('You do not have access to this page.'), path).toBeVisible();
  }
  expect((await page.request.get('/api/staff')).status()).toBe(403);
  expect((await page.request.put('/api/customers/settings', { data: { enabled: true, rupeesPerPoint: 100, pointValue: 1 } })).status()).toBe(403);
});

test('A6 a kitchen device only gets the kitchen screens', async ({ browser }) => {
  const staff = await addStaff(api, 'kitchen', `Chef ${uid()}`);
  const page = await pageAs(browser, staff.email, staff.password);
  await page.goto('/orders');
  await expect(page.getByText('You do not have access to this page.')).toBeVisible();
  await page.goto('/customers');
  await expect(page.getByText('You do not have access to this page.')).toBeVisible();
  expect((await page.request.post('/api/orders', { data: { orderType: 'Takeaway', customerName: 'X', clientOrderId: crypto.randomUUID(), items: [] } })).status()).toBe(403);
});

test('A7 after signing out the pages and API are closed again', async ({ page }) => {
  await signIn(page);
  await page.goto('/orders');
  await expect(page).toHaveURL(/\/orders/);
  expect((await page.request.post('/api/auth/logout')).status()).toBe(200);
  expect((await page.request.get('/api/orders')).status()).toBe(401);
  await page.goto('/orders');
  await expect(page).toHaveURL(/\/login/);
});

test('A8 signup refuses bad details and a taken email', async () => {
  const anon = await anonymous();
  const good = { restaurantName: `QA Cafe ${uid()}`, ownerName: 'Kiran', ownerEmail: `qa-${uid()}@test.local`, ownerPhone: '9825012345', ownerPassword: 'Owner#2026xy' };
  for (const bad of [
    { ...good, ownerPhone: '12345' },
    { ...good, ownerPhone: '5825012345' },
    { ...good, ownerPassword: 'abc' },
    { ...good, ownerEmail: 'not-an-email' },
    { ...good, restaurantName: '' },
    { ...good, ownerEmail: OWNER_EMAIL },
    {},
  ]) {
    const res = await anon.post('/api/signup', { data: bad });
    expect(res.status(), JSON.stringify(bad)).toBe(400);
  }
  // A bot that fills the hidden field gets a quiet OK, but no restaurant is made.
  const botEmail = `bot-${uid()}@test.local`;
  expect((await anon.post('/api/signup', { data: { ...good, ownerEmail: botEmail, website: 'http://spam.example' } })).status()).toBe(200);
  expect(await loginStatus(anon, { email: botEmail, password: good.ownerPassword })).toBe(401);
  await anon.dispose();
});

test('A9 owner reset without a valid code never changes the password', async () => {
  const anon = await anonymous();
  const res = await anon.post('/api/auth/owner-reset', { data: { email: OWNER_EMAIL, code: '000000', newPassword: 'Hacked#12345' } });
  expect([400, 404]).toContain(res.status());
  const empty = await anon.post('/api/auth/owner-reset', { data: {} });
  expect([400, 404]).toContain(empty.status());
  expect(await loginStatus(anon, { email: OWNER_EMAIL, password: OWNER_PASSWORD })).toBe(200);
  await anon.dispose();
});

test('A10 the reset-password and signup pages open without signing in', async ({ page }) => {
  await page.goto('/reset-password');
  await expect(page).toHaveURL(/\/reset-password/);
  await expect(page.locator('h1')).toBeVisible();
  await page.goto('/signup');
  await expect(page.getByRole('heading', { name: 'Start your free trial' })).toBeVisible();
  await page.getByRole('button', { name: 'Start free trial' }).click();
  await expect(page).toHaveURL(/\/signup/);
});
