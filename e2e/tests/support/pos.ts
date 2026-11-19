import { APIRequestContext, APIResponse, Browser, Page, expect, request } from '@playwright/test';

// Shared helpers for the 1.9 end-to-end tests. Test data is made through the API (fast), the checks go through the screens.
export const BASE_URL = process.env.BASE_URL ?? 'http://localhost:8080';
export const OWNER_EMAIL = process.env.E2E_EMAIL ?? 'admin@pos.local';
export const OWNER_PASSWORD = process.env.E2E_PASSWORD ?? 'ChangeMe#12345';
export const STAFF_PASSWORD = 'Test#1234';
export const MANAGER_PIN = '4321';

// Unique suffix so tests never collide with each other or with earlier runs.
export const uid = () => `${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;

export async function signIn(page: Page, email = OWNER_EMAIL, password = OWNER_PASSWORD) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/login'));
}

export async function pageAs(browser: Browser, email = OWNER_EMAIL, password = OWNER_PASSWORD) {
  const page = await (await browser.newContext()).newPage();
  await signIn(page, email, password);
  return page;
}

export async function ok(res: APIResponse) {
  const text = await res.text();
  expect(res.ok(), `${res.url()} -> ${res.status()} ${text}`).toBeTruthy();
  return text ? JSON.parse(text) : null;
}

export async function ownerApi(): Promise<APIRequestContext> {
  const api = await request.newContext({ baseURL: BASE_URL });
  await ok(await api.post('/api/auth/login', { data: { email: OWNER_EMAIL, password: OWNER_PASSWORD } }));
  return api;
}

export async function addCategory(api: APIRequestContext, name: string, extra: Record<string, unknown> = {}) {
  return ok(await api.post('/api/admin/menu/categories', { data: { name, sortOrder: 50, isActive: true, ...extra } }));
}

export async function addItem(api: APIRequestContext, data: Record<string, unknown>) {
  return ok(await api.post('/api/admin/menu/items', {
    data: { description: null, isVeg: true, isAvailable: true, taxRate: 5, sortOrder: 50, ...data },
  }));
}

export async function addStation(api: APIRequestContext, name: string) {
  return ok(await api.post('/api/admin/menu/stations', { data: { name, sortOrder: 50, isActive: true } }));
}

export async function addStaff(api: APIRequestContext, role: string, displayName: string) {
  const email = `${role}-${uid()}@test.local`;
  const staff = await ok(await api.post('/api/staff', { data: { email, displayName, role, password: STAFF_PASSWORD, isDevice: false } }));
  return { ...staff, email, password: STAFF_PASSWORD };
}

export async function setPin(api: APIRequestContext, staffId: string, pin = MANAGER_PIN) {
  await ok(await api.put(`/api/staff/${staffId}/pin`, { data: { pin } }));
}

// Takeaway order from the POS screen (no table needed). Returns the token number.
export async function placeTakeaway(page: Page, customer: string, items: string[]) {
  await page.goto('/pos');
  await page.getByRole('button', { name: 'Takeaway', exact: true }).click();
  await page.getByLabel('Customer name').fill(customer);
  for (const name of items) await page.getByRole('button', { name: `Add ${name}`, exact: true }).click();
  await page.getByRole('button', { name: 'Place order' }).click();
  return placedToken(page);
}

export async function placedToken(page: Page) {
  const placed = page.getByText(/Order placed\. Token \d+/).last();
  await expect(placed).toBeVisible();
  return /Token (\d+)/.exec((await placed.textContent()) ?? '')?.[1] ?? '';
}

export const ticket = (kitchen: Page, token: string) => kitchen.getByRole('article', { name: `Token ${token}`, exact: true });

// India time, hours from now, as HH:MM (for time-based menu tests).
export function istTime(hoursFromNow: number, minute = 0) {
  const h = (new Date(Date.now() + 5.5 * 3600e3).getUTCHours() + hoursFromNow + 24) % 24;
  return `${String(h).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
}
