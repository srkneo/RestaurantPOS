import { test, expect, Page } from '@playwright/test';
import { BASE_URL, pageAs } from './support/pos';

// Security: every page carries a Content-Security-Policy, and no page breaks it (a broken rule shows in the console).
const PAGES = ['/pos', '/orders', '/customers', '/kitchen', '/tokens', '/admin/menu', '/stock', '/admin/tables/qr', '/admin/reports',
  '/admin/settings', '/admin/billing', '/about', '/help'];

function cspErrors(page: Page) {
  const found: string[] = [];
  page.on('console', (m) => { if (/Content Security Policy|Refused to (load|execute|apply|connect)/i.test(m.text())) found.push(`${page.url()}: ${m.text()}`); });
  return found;
}

test('C1 pages send a Content-Security-Policy and break none of its rules', async ({ browser, request }) => {
  const res = await request.get(`${BASE_URL}/pos`);
  const csp = res.headers()['content-security-policy'] ?? '';
  expect(csp).toContain("script-src 'self'");
  expect(csp).toContain("frame-ancestors 'none'");

  const page = await pageAs(browser);
  const errors = cspErrors(page);
  for (const path of PAGES) {
    await page.goto(path);
    await expect(page.getByText('Loading...', { exact: true }), path).toHaveCount(0, { timeout: 10_000 });
  }
  expect(errors).toEqual([]);
});

test('C2 public pages keep the policy too', async ({ page }) => {
  const errors = cspErrors(page);
  for (const path of ['/login', '/signup', '/pricing', '/terms', '/privacy']) {
    const res = await page.goto(path);
    expect(res?.headers()['content-security-policy'] ?? '', path).toContain("default-src 'self'");
  }
  expect(errors).toEqual([]);
});
