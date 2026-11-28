import { test, expect, request } from '@playwright/test';
import { BASE_URL, addStaff, ok, ownerApi, uid } from './support/pos';

// v2.4 RN2: the authenticator app shows AllInOne POS for new setups; roles without the authenticator option are refused.
async function signedIn(email: string, password: string) {
  const ctx = await request.newContext({ baseURL: BASE_URL });
  await ok(await ctx.post('/api/auth/login', { data: { email, password } }));
  return ctx;
}

test('RN2 the authenticator app setup names AllInOne POS', async () => {
  const api = await ownerApi();
  const manager = await addStaff(api, 'manager', `Issuer ${uid()}`);
  const ctx = await signedIn(manager.email, manager.password);
  const setup = await ok(await ctx.post('/api/auth/2fa/setup', { data: {} }));
  expect(setup.uri).toMatch(/^otpauth:\/\/totp\/AllInOne%20POS:/);
  expect(setup.uri).toContain('&issuer=AllInOne%20POS&');
  expect(setup.uri).not.toContain('Restaurant');
});

test('RN2 negative: a cashier cannot set up the authenticator app', async () => {
  const api = await ownerApi();
  const cashier = await addStaff(api, 'cashier', `NoApp ${uid()}`);
  const ctx = await signedIn(cashier.email, cashier.password);
  const res = await ctx.post('/api/auth/2fa/setup', { data: {} });
  expect(res.status()).toBe(403);
});
