import { test, expect, Page } from '@playwright/test';
import { addCategory, addItem, ok, ownerApi, signIn, uid } from './support/pos';

// Layout checks for every 1.9 screen at phone, tablet and desktop size:
// the page shows its main heading or action, nothing sticks out sideways, and dialogs fit on the screen.

const sizes = [
  { name: 'phone', width: 390, height: 844 },
  { name: 'tablet', width: 820, height: 1180 },
  { name: 'desktop', width: 1366, height: 768 },
];

// Staff screens with their main heading (level 1).
const staffPages: { path: string; heading: string | RegExp }[] = [
  { path: '/orders', heading: 'Orders' },
  { path: '/kitchen', heading: 'Kitchen' },
  { path: '/day-close', heading: /Day close/ },
  { path: '/expenses', heading: /Expenses/ },
  { path: '/reports/day', heading: /Day report|End of day|Z report/i },
  { path: '/admin/menu', heading: 'Menu' },
  { path: '/admin/tables', heading: 'Tables' },
  { path: '/admin/tables/qr', heading: 'Table QR codes' },
  { path: '/admin/staff', heading: 'Staff' },
  { path: '/admin/store', heading: 'Store profile' },
  { path: '/admin/billing', heading: 'Billing settings' },
  { path: '/admin/reports', heading: /reports/i },
  { path: '/admin/feedback', heading: 'Guest feedback' },
  { path: '/admin/setup', heading: /Setup|Get started|checklist/i },
];

async function overflow(page: Page) {
  return page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
}

async function fitsWidth(page: Page, box: { x: number; width: number } | null) {
  expect(box, 'element is on the page').not.toBeNull();
  const width = page.viewportSize()!.width;
  expect(box!.x).toBeGreaterThanOrEqual(-1);
  expect(box!.x + box!.width).toBeLessThanOrEqual(width + 1);
}

for (const size of sizes) {
  test.describe(`${size.name} ${size.width}x${size.height}`, () => {
    test.use({ viewport: { width: size.width, height: size.height } });

    test('staff screens fit without sideways scrolling', async ({ page }) => {
      test.setTimeout(120_000);
      await signIn(page);
      const problems: string[] = [];
      for (const p of staffPages) {
        await page.goto(p.path);
        const heading = page.getByRole('heading', { level: 1, name: p.heading }).first();
        try {
          await expect(heading).toBeVisible({ timeout: 10_000 });
        } catch {
          problems.push(`${p.path}: heading not visible`);
          continue;
        }
        const extra = await overflow(page);
        if (extra > 1) problems.push(`${p.path}: ${extra}px wider than the screen`);
      }
      expect(problems, problems.join('\n')).toEqual([]);
    });

    test('POS: menu, order button and table grid fit', async ({ page }) => {
      await signIn(page);
      await page.goto('/pos');
      await expect(page.getByRole('button', { name: 'Add Fries', exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Add Fries', exact: true }).click();
      await expect(page.getByRole('button', { name: 'Place order' }).first()).toBeVisible();
      expect(await overflow(page)).toBeLessThanOrEqual(1);

      await page.getByLabel('Table', { exact: true }).click();
      const grid = page.getByRole('dialog', { name: 'Tables' });
      await expect(grid).toBeVisible();
      await fitsWidth(page, await grid.boundingBox());
      await grid.getByRole('button', { name: 'Close' }).click();
    });

    test('bill dialog fits on the screen', async ({ page }) => {
      const api = await ownerApi();
      const id = uid();
      const category = await addCategory(api, `Fit ${id}`);
      const item = await addItem(api, { name: `Fit Dal ${id}`, price: 120, categoryId: category.id });
      await ok(await api.post('/api/orders', {
        data: { orderType: 'Takeaway', customerName: `Fit ${id}`, clientOrderId: crypto.randomUUID(), items: [{ menuItemId: item.id, quantity: 1 }] },
      }));
      await signIn(page);
      await page.goto('/orders');
      const row = page.getByRole('row').filter({ hasText: `Fit ${id}` });
      await row.getByRole('button', { name: 'Bill', exact: true }).click();
      const dialog = page.getByRole('dialog');
      await expect(dialog.getByRole('button', { name: 'Make bill' })).toBeVisible();
      await fitsWidth(page, await dialog.boundingBox());
      await dialog.getByRole('button', { name: 'Make bill' }).click();
      await expect(dialog.getByRole('button', { name: 'Send on WhatsApp' })).toBeVisible();
      await fitsWidth(page, await dialog.boundingBox());
    });

    test('guest QR menu and feedback pages fit', async ({ browser }) => {
      const api = await ownerApi();
      const id = uid();
      await ok(await api.put('/api/admin/tables/qr-setting', { data: { enabled: true } }));
      const table = await ok(await api.post('/api/admin/tables', { data: { tableNumber: `V${id}`, isActive: true, sortOrder: 990 } }));
      const qr = (await ok(await api.get('/api/admin/tables/qr'))).find((t: { id: number }) => t.id === table.id);

      const context = await browser.newContext({ viewport: { width: size.width, height: size.height } });
      const guest = await context.newPage();
      await guest.goto(`/t/${qr.token}`);
      await expect(guest.getByRole('button', { name: 'Add Fries', exact: true })).toBeVisible();
      await guest.getByRole('button', { name: 'Add Fries', exact: true }).click();
      await expect(guest.getByRole('button', { name: 'Send order' })).toBeVisible();
      expect(await overflow(guest)).toBeLessThanOrEqual(1);

      await ok(await api.put('/api/admin/feedback-setting', { data: { enabled: true } }));
      try {
        const category = await addCategory(api, `FitG ${id}`);
        const item = await addItem(api, { name: `FitG Tea ${id}`, price: 30, categoryId: category.id });
        const order = await ok(await api.post('/api/orders', {
          data: { orderType: 'Takeaway', customerName: `FitG ${id}`, clientOrderId: crypto.randomUUID(), items: [{ menuItemId: item.id, quantity: 1 }] },
        }));
        const bill = await ok(await api.post(`/api/orders/${order.orderId}/bill`, { data: {} }));
        const token = (await ok(await api.get(`/api/bills/${bill.id}/feedback-link`))).token;
        await guest.goto(`/feedback/${token}`);
        await expect(guest.getByRole('button', { name: 'Send feedback' })).toBeVisible();
        for (const n of [1, 5]) await expect(guest.getByRole('button', { name: `${n} stars`, exact: true })).toBeInViewport();
        expect(await overflow(guest)).toBeLessThanOrEqual(1);
      } finally {
        // Feedback stays on; guests-19 sets it the way each of its tests needs.
      }
    });

    test('token screen fits', async ({ page }) => {
      await signIn(page);
      await page.goto('/tokens');
      await expect(page.getByRole('heading', { name: 'Order status', level: 1 })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Preparing' })).toBeInViewport();
      await expect(page.getByRole('heading', { name: 'Ready to collect' })).toBeInViewport();
      expect(await overflow(page)).toBeLessThanOrEqual(1);
    });
  });
}
