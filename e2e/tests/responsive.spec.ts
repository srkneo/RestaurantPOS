import { test, expect, Page } from '@playwright/test';

const email = process.env.E2E_EMAIL ?? 'admin@pos.local';
const password = process.env.E2E_PASSWORD ?? 'ChangeMe#12345';

const sizes = [
  { name: 'phone', width: 390, height: 844 },
  { name: 'tablet', width: 1180, height: 820 },
  { name: 'desktop', width: 1440, height: 900 },
];

async function signIn(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/login'));
}

async function horizontalOverflow(page: Page) {
  return page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
}

for (const size of sizes) {
  test(`screens fit on ${size.name} (${size.width}x${size.height})`, async ({ page }) => {
    await page.setViewportSize({ width: size.width, height: size.height });
    await signIn(page);

    await page.goto('/pos');
    const place = page.getByRole('button', { name: 'Place order' });
    await expect(place).toBeVisible();
    if (size.width >= 992) {
      // Order panel stays on screen next to the menu, no scrolling needed.
      await expect(place).toBeInViewport();
    }
    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(1);

    await page.goto('/orders');
    await expect(page.getByRole('heading', { name: 'Orders', exact: true })).toBeVisible();
    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(1);

    await page.goto('/kitchen');
    await expect(page.getByRole('heading', { name: 'Kitchen', exact: true })).toBeVisible();
    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(1);
  });
}
