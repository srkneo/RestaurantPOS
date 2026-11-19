import { test, expect } from '@playwright/test';
import { addCategory, addItem, ok, ownerApi, signIn, uid } from './support/pos';

// v2.1 recipes: the owner sets what one portion uses; completing an order takes it from stock.
test('ST2 recipe on the Menu page takes stock when an order is completed', async ({ page }) => {
  const id = uid();
  const api = await ownerApi();
  const milk = await ok(await api.post('/api/stock', { data: { name: `Recipe milk ${id}`, unit: 'l', quantity: 10, lowLevel: 0 } }));
  const category = await addCategory(api, `Recipe tea ${id}`);
  const chai = await addItem(api, { name: `Recipe chai ${id}`, price: 20, categoryId: category.id });

  await signIn(page);
  await page.goto('/admin/menu');
  await page.locator('#menu-search').fill(`Recipe chai ${id}`);
  await page.getByRole('row', { name: new RegExp(`Recipe chai ${id}`) }).getByRole('button', { name: 'Recipe', exact: true }).click();
  await expect(page.getByRole('heading', { name: `Recipe of Recipe chai ${id}` })).toBeVisible();
  await page.getByLabel('Stock item 1').selectOption(String(milk.id));
  await page.getByLabel('Quantity 1').fill('0.25');
  await page.getByRole('button', { name: 'Save recipe' }).click();
  await expect(page.getByText(`Recipe of Recipe chai ${id} saved.`)).toBeVisible();

  const order = await ok(await api.post('/api/orders', {
    data: { orderType: 'Takeaway', customerName: `Recipe ${id}`, clientOrderId: crypto.randomUUID(), items: [{ menuItemId: chai.id, quantity: 2 }] },
  }));
  await ok(await api.put(`/api/orders/${order.orderId}/status`, { data: { status: 'Completed' } }));

  const list = await ok(await api.get('/api/stock'));
  expect(list.find((s: { id: number }) => s.id === milk.id).quantity).toBe(9.5);
});
