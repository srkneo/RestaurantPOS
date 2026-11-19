import { test, expect, APIRequestContext, Browser, Page } from '@playwright/test';
import { addCategory, addItem, ok, ownerApi, pageAs, signIn, uid } from './support/pos';

// 1.9 test cases F1-F7 and G1-G3: Google review QR, feedback QR and guest page, low-rating alert,
// Feedback page and CSV, day report and Reports with the guest rating, Tally export.

test.describe.configure({ mode: 'serial' });

let api: APIRequestContext;
let itemId: number;
let id: string;

test.beforeAll(async () => {
  api = await ownerApi();
  id = uid();
  const category = await addCategory(api, `Guests ${id}`);
  itemId = (await addItem(api, { name: `Dal ${id}`, price: 180, categoryId: category.id })).id;
});

test.afterAll(async () => {
  await api.put('/api/admin/feedback-setting', { data: { enabled: false } });
  await api.put('/api/admin/review-link', { data: { reviewUrl: '' } });
});

async function bill(customer: string) {
  const order = await ok(await api.post('/api/orders', {
    data: { orderType: 'Takeaway', customerName: customer, clientOrderId: crypto.randomUUID(), items: [{ menuItemId: itemId, quantity: 1 }] },
  }));
  return ok(await api.post(`/api/orders/${order.orderId}/bill`, { data: {} }));
}

async function feedbackToken(billId: number) {
  return (await ok(await api.get(`/api/bills/${billId}/feedback-link`))).token as string;
}

async function guestPage(browser: Browser, token: string) {
  const page = await (await browser.newContext()).newPage();
  await page.goto(`/feedback/${token}`);
  return page;
}

async function rate(page: Page, stars: number, comment: string) {
  await page.getByRole('button', { name: `${stars} stars`, exact: true }).click();
  await page.getByLabel('Anything to tell us? (optional)').fill(comment);
  await page.getByRole('button', { name: 'Send feedback' }).click();
  await expect(page.getByText('Thank you for your feedback!')).toBeVisible();
}

test('F1 Google review link: saved on the Store page and printed on the bill', async ({ browser }) => {
  const owner = await pageAs(browser);
  await owner.goto('/admin/store');
  const link = owner.getByLabel('Google review link');
  await link.fill('abc');
  await owner.getByRole('button', { name: 'Save link' }).click();
  await expect(owner.getByText('Paste the full link starting with https://')).toBeVisible();
  await link.fill('https://g.page/r/anand-thali-house/review');
  await owner.getByRole('button', { name: 'Save link' }).click();
  await expect(owner.getByText('Review link saved.')).toBeVisible();

  const b = await bill(`Review ${id}`);
  await owner.goto(`/print/bill/${b.id}`);
  await expect(owner.getByAltText('Rate us on Google')).toBeVisible();
});

test('F2 feedback switch: bills print the feedback QR', async ({ browser }) => {
  const owner = await pageAs(browser);
  await owner.goto('/admin/store');
  const sw = owner.getByLabel('Print a feedback QR on every bill');
  if (!(await sw.isChecked())) await sw.check();
  await expect(owner.getByText('Feedback QR turned on.')).toBeVisible();

  const b = await bill(`Print ${id}`);
  await owner.goto(`/print/bill/${b.id}`);
  await expect(owner.getByAltText('Tell us how we did')).toBeVisible();
});

test('F3 a guest rates once from the feedback page', async ({ browser }) => {
  const b = await bill(`Guest ${id}`);
  const page = await guestPage(browser, await feedbackToken(b.id));
  await expect(page.getByText(`Bill ${b.invoiceNumber}`)).toBeVisible();
  await page.getByRole('button', { name: 'Send feedback' }).click();
  await expect(page.getByRole('alert')).toHaveText('Tap the stars to rate your visit.');
  await rate(page, 4, 'Dal makhani was very tasty');
  await page.reload();
  await expect(page.getByText('Thank you, we already have your feedback for this bill.')).toBeVisible();
});

test('F4 a 1-2 star rating alerts the POS', async ({ browser }) => {
  const pos = await pageAs(browser);
  await pos.goto('/pos');
  await expect(pos.getByRole('button', { name: 'Add Fries', exact: true })).toBeVisible();
  const b = await bill(`Cold ${id}`);
  const page = await guestPage(browser, await feedbackToken(b.id));
  await rate(page, 2, 'Food was cold');
  await expect(pos.getByText(new RegExp(`Low rating: 2 star\\(s\\).*bill ${b.invoiceNumber.replace(/\//g, '\\/')}`))).toBeVisible({ timeout: 10_000 });
});

test('F5 the Feedback page shows the average and downloads a CSV', async ({ browser }) => {
  const owner = await pageAs(browser);
  await owner.goto('/admin/feedback');
  await expect(owner.getByText('Average 3.0 of 5 from 2 guests')).toBeVisible();
  await expect(owner.getByText('Dal makhani was very tasty')).toBeVisible();
  await expect(owner.getByRole('link', { name: 'Download (CSV)' })).toHaveAttribute('href', '/api/admin/feedback.csv');
  const csv = await owner.request.get('/api/admin/feedback.csv');
  expect(csv.ok()).toBeTruthy();
  expect(await csv.text()).toContain('Food was cold');
});

test('F6 the WhatsApp bill carries the feedback link', async ({ browser }) => {
  const context = await browser.newContext();
  await context.addInitScript(() => {
    (window as unknown as { __opened: string[] }).__opened = [];
    window.open = ((url?: string | URL) => { (window as unknown as { __opened: string[] }).__opened.push(String(url)); return null; }) as typeof window.open;
  });
  const page = await context.newPage();
  await signIn(page);
  const b = await bill(`Share ${id}`);
  await page.goto('/orders');
  const row = page.getByRole('row').filter({ hasText: `Share ${id}` });
  const linkLoaded = page.waitForResponse((r) => r.url().includes(`/api/bills/${b.id}/feedback-link`));
  await row.getByRole('button', { name: 'Bill', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: `Bill ${b.invoiceNumber}` })).toBeVisible();
  await linkLoaded;
  await dialog.getByRole('button', { name: 'Send on WhatsApp' }).click();
  const url = await page.evaluate(() => (window as unknown as { __opened: string[] }).__opened.find((u) => u.startsWith('https://wa.me/')) ?? '');
  expect(decodeURIComponent(url)).toMatch(/Tell us how we did: https?:\/\/[^\s]+\/feedback\//);
});

test('F7 switching feedback off stops old links', async ({ browser }) => {
  const b = await bill(`Off ${id}`);
  const token = await feedbackToken(b.id);
  await ok(await api.put('/api/admin/feedback-setting', { data: { enabled: false } }));
  const page = await guestPage(browser, token);
  await expect(page.getByText('This feedback link does not work any more.')).toBeVisible();
  await ok(await api.put('/api/admin/feedback-setting', { data: { enabled: true } }));
});

test('G1 day report: WhatsApp summary includes the guest rating', async ({ browser }) => {
  const context = await browser.newContext();
  await context.addInitScript(() => {
    (window as unknown as { __opened: string[] }).__opened = [];
    window.open = ((url?: string | URL) => { (window as unknown as { __opened: string[] }).__opened.push(String(url)); return null; }) as typeof window.open;
  });
  const page = await context.newPage();
  await signIn(page);
  await page.goto('/reports/day');
  await page.getByRole('button', { name: 'Share on WhatsApp' }).click();
  const url = await page.evaluate(() => (window as unknown as { __opened: string[] }).__opened[0] ?? '');
  expect(decodeURIComponent(url)).toContain('Guest rating 3.0 of 5 (2 guests)');
});

test('G2 Reports page shows the guest rating in the summary', async ({ browser }) => {
  const owner = await pageAs(browser);
  await owner.goto('/admin/reports');
  const summary = owner.getByRole('region', { name: 'Summary' });
  await expect(summary.getByText('Guest rating')).toBeVisible();
  await expect(summary.getByText('3.0 / 5 (2)')).toBeVisible();
  await expect(owner.getByRole('link', { name: 'Download for Tally (XML)' })).toBeVisible();
});

test('G3 Tally export balances every voucher', async () => {
  const res = await api.get('/api/reports/tally.xml');
  expect(res.ok()).toBeTruthy();
  const xml = await res.text();
  expect(xml).toContain('<VOUCHER VCHTYPE="Sales"');
  const vouchers = xml.split('<VOUCHER ').slice(1);
  for (const v of vouchers) {
    const amounts = [...v.matchAll(/<AMOUNT>(-?[\d.]+)<\/AMOUNT>/g)].map((m) => Number(m[1]));
    expect(Math.abs(amounts.reduce((a, b) => a + b, 0))).toBeLessThan(0.005);
  }
});
