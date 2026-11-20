import { test, Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { ownerApi, signIn } from './support/pos';

// Screenshots for the user guide, not a test: runs only with SHOTS=1 (scripts/guide-shots.ps1), after seed-data.
// Every step is best effort, so one missing page never stops the others.
test.skip(process.env.SHOTS !== '1', 'guide screenshots only when SHOTS=1');
test.setTimeout(10 * 60 * 1000);

const OUT = resolve(process.cwd(), '..', '..', 'guide-screens');
const two = (n: number) => String(n).padStart(2, '0');
const local = (d: Date) => `${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())}`;

async function shot(page: Page, name: string, url?: string, before?: () => Promise<void>) {
  try {
    if (url) await page.goto(url);
    await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
    if (before) await before();
    await page.waitForTimeout(800);
    await page.screenshot({ path: resolve(OUT, `${name}.png`), fullPage: true });
  } catch (e) {
    console.log(`Screenshot ${name} skipped: ${(e as Error).message}`);
  }
}

test('guide screenshots', async ({ browser }) => {
  mkdirSync(OUT, { recursive: true });
  const api = await ownerApi();

  // Extra sample data for the newer pages: stock with a low item, a recipe and two reservations.
  try {
    const add = async (name: string, unit: string, quantity: number, lowLevel: number) => {
      const res = await api.post('/api/stock', { data: { name, unit, quantity, lowLevel } });
      return res.ok() ? (await res.json()).id : null;
    };
    const milk = await add('Milk', 'l', 12, 5);
    await add('Paneer', 'kg', 1.5, 2);
    await add('Sugar', 'kg', 10, 2);
    await add('Paper cups', 'pcs', 400, 100);
    if (milk) {
      await api.post(`/api/stock/${milk}/moves`, { data: { kind: 'Purchase', quantity: 20, cost: 1100, supplier: 'Amul Dairy' } });
      await api.post(`/api/stock/${milk}/moves`, { data: { kind: 'Waste', quantity: 1, note: 'Spoilt' } });
      const menu = await (await api.get('/api/menuitems')).json();
      const tea = (Array.isArray(menu) ? menu : []).find((m: { name: string }) => m.name === 'Masala Tea');
      if (tea) await api.put(`/api/menu/${tea.id}/recipe`, { data: { lines: [{ stockItemId: milk, quantity: 0.15 }] } });
    }
    const tables = await (await api.get('/api/tables')).json();
    const tableId = Array.isArray(tables) && tables.length > 2 ? tables[2].id : null;
    const soon = new Date(Date.now() + 90 * 60000);
    await api.post('/api/reservations', { data: { customerName: 'Patel family', phone: '98250 12345', people: 4, date: local(soon), time: `${two(soon.getHours())}:${two(soon.getMinutes())}`, tableId, note: 'Birthday' } });
    await api.post('/api/reservations', { data: { customerName: 'Shah', people: 2, date: local(soon), time: '21:30', note: 'Window seat' } });
  } catch (e) {
    console.log(`Extra sample data skipped: ${(e as Error).message}`);
  }

  // Signed out: sign-in, pricing, signup.
  const guest = await browser.newPage({ viewport: { width: 1366, height: 800 } });
  await shot(guest, '01-sign-in', '/login');
  await shot(guest, '02-pricing', '/pricing');
  await shot(guest, '03-signup', '/signup');
  await shot(guest, '04-terms', '/terms');
  await guest.close();

  // Owner on a desktop screen.
  const page = await browser.newPage({ viewport: { width: 1366, height: 800 } });
  await signIn(page);
  await shot(page, '10-menu-group-open', '/pos', async () => { await page.getByRole('button', { name: 'Admin', exact: true }).click(); });
  await shot(page, '11-pos', '/pos');
  await shot(page, '12-pos-table-grid', '/pos', async () => { await page.locator('#pos-table').click(); });
  await shot(page, '13-orders', '/orders');
  await shot(page, '14-kitchen', '/kitchen');
  await shot(page, '15-token-screen', '/tokens');
  await shot(page, '16-day-close', '/day-close');
  await shot(page, '17-day-report', '/reports/day');
  await shot(page, '18-expenses', '/expenses');
  await shot(page, '19-reservations', '/reservations');
  await shot(page, '20-stock', '/stock');
  await shot(page, '21-stock-record', '/stock', async () => { await page.getByRole('button', { name: 'Record for Milk' }).click(); });
  await shot(page, '22-stock-report', '/stock/report');
  await shot(page, '23-menu', '/admin/menu');
  await shot(page, '24-menu-recipe', '/admin/menu', async () => {
    await page.locator('#menu-search').fill('Masala Tea');
    await page.getByRole('row', { name: /Masala Tea/ }).getByRole('button', { name: 'Recipe', exact: true }).click();
  });
  await shot(page, '25-tables', '/admin/tables');
  await shot(page, '26-reports', '/admin/reports');
  await shot(page, '27-feedback', '/admin/feedback');
  await shot(page, '30-setup', '/admin/setup');
  await shot(page, '31-staff', '/admin/staff');
  await shot(page, '32-store', '/admin/store');
  await shot(page, '33-settings', '/admin/settings');
  await shot(page, '34-billing-settings', '/admin/billing');
  await shot(page, '35-help', '/about');
  await page.close();

  // Waiter view on a phone.
  const phone = await browser.newPage({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  await signIn(phone);
  await shot(phone, '40-pos-phone', '/pos');
  await shot(phone, '41-reservations-phone', '/reservations');
  await phone.close();
});
