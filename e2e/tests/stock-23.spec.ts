import { test, expect } from '@playwright/test';
import { signIn, uid } from './support/pos';

// v2.1 stock: the owner adds an item, records waste until it is low, then sees it in the history.
test('ST1 owner adds a stock item, records waste and sees the low-stock mark and history', async ({ page }) => {
  const name = `Milk ${uid()}`;
  await signIn(page);
  await page.goto('/stock');
  await expect(page.getByRole('heading', { name: 'Stock' })).toBeVisible();

  await page.getByRole('button', { name: 'Add stock item' }).click();
  await page.getByLabel('Item').fill(name);
  await page.getByLabel('Unit').selectOption('l');
  await page.getByLabel('Quantity in stock now').fill('10');
  await page.getByLabel('Warn when at or below').fill('5');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText(`${name}: 10 l in stock.`)).toBeVisible();

  await page.getByRole('button', { name: `Record for ${name}` }).click();
  await page.getByLabel('What happened').selectOption('Waste');
  await page.getByLabel('Quantity (l)').fill('6');
  await page.getByLabel('Note').fill('Spoilt');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText(`${name}: 4 l in stock.`)).toBeVisible();
  await expect(page.getByRole('row', { name: new RegExp(name) }).getByText('Low')).toBeVisible();

  await page.getByRole('button', { name: `History of ${name}` }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Spoilt')).toBeVisible();
  await expect(dialog.getByText('Opening stock')).toBeVisible();
  await dialog.getByRole('button', { name: 'Close' }).click();
});
