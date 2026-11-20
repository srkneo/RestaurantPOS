import { test, expect } from '@playwright/test';
import { signIn, uid } from './support/pos';

// v2.1 reservations: book for tomorrow, see it on that day, seat the guests.
test('R1 owner books a table for tomorrow evening and seats the guests', async ({ page }) => {
  const name = `Guest ${uid()}`;
  const d = new Date();
  d.setDate(d.getDate() + 1);
  const tomorrow = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

  await signIn(page);
  await page.goto('/reservations');
  await expect(page.getByRole('heading', { name: 'Reservations' })).toBeVisible();
  await page.getByRole('button', { name: 'New reservation' }).click();
  await page.getByLabel('Guest name', { exact: true }).fill(name);
  await page.getByLabel('People', { exact: true }).fill('4');
  await page.getByLabel('Date', { exact: true }).fill(tomorrow);
  await page.getByLabel('Time', { exact: true }).fill('20:30');
  await page.getByLabel('Note', { exact: true }).fill('Window seat');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText(`Booked: ${name} at 20:30.`)).toBeVisible();

  await expect(page.getByLabel('Day', { exact: true })).toHaveValue(tomorrow);
  const row = page.getByRole('row', { name: new RegExp(name) });
  await expect(row).toContainText('Window seat');
  await row.getByRole('button', { name: `Seat ${name}` }).click();
  await expect(row).toContainText('Seated');
});
