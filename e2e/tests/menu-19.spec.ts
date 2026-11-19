import { test, expect } from '@playwright/test';
import { addCategory, addItem, addStation, ok, ownerApi, pageAs, placeTakeaway, signIn, ticket, uid, istTime } from './support/pos';

// 1.9 test cases B1-B4: sizes and extras, kitchen stations, time-based menu, combos.

test('B1 sizes and extras: chosen on the POS, shown in the cart and on the kitchen screen', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  const category = await addCategory(api, `Pizzas ${id}`);
  const pizza = await addItem(api, { name: `Pizza ${id}`, price: 200, categoryId: category.id });
  await ok(await api.put(`/api/admin/menu/items/${pizza.id}/options`, {
    data: {
      variants: [{ name: 'Regular', price: 200 }, { name: 'Large', price: 320 }],
      addOns: [{ name: 'Extra cheese', price: 30 }, { name: 'Jalapeno', price: 20 }],
    },
  }));

  const kitchen = await pageAs(browser);
  await kitchen.goto('/kitchen');
  const pos = await pageAs(browser);
  await pos.goto('/pos');
  await pos.getByRole('button', { name: 'Takeaway', exact: true }).click();
  await pos.getByLabel('Customer name').fill(`Kiran ${id}`);
  await pos.getByRole('button', { name: `Add Pizza ${id}`, exact: true }).click();
  const dialog = pos.getByRole('dialog');
  await dialog.getByRole('button', { name: /^Large/ }).click();
  await dialog.getByRole('button', { name: /^Extra cheese/ }).click();
  await dialog.getByRole('button', { name: 'Add · ₹350.00' }).click();
  await expect(pos.getByText(`Pizza ${id} (Large)`).first()).toBeVisible();
  await expect(pos.getByText('Extra cheese').first()).toBeVisible();
  await pos.getByRole('button', { name: 'Place order' }).click();
  const token = /Token (\d+)/.exec((await pos.getByText(/Order placed\. Token \d+/).last().textContent()) ?? '')?.[1] ?? '';

  const t = ticket(kitchen, token);
  await expect(t).toBeVisible({ timeout: 10_000 });
  await expect(t.getByText(new RegExp(`^1 x Pizza ${id}`))).toBeVisible();
  await expect(t.getByText('+ Extra cheese')).toBeVisible();
});

test('B2 kitchen station: the station screen shows only its own items', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  const station = await addStation(api, `Tandoor ${id}`);
  const category = await addCategory(api, `Starters ${id}`);
  await addItem(api, { name: `Paneer Tikka ${id}`, price: 220, categoryId: category.id, stationId: station.id });
  await addItem(api, { name: `Dal Makhani ${id}`, price: 180, categoryId: category.id });

  const pos = await pageAs(browser);
  const token = await placeTakeaway(pos, `Ravi ${id}`, [`Paneer Tikka ${id}`, `Dal Makhani ${id}`]);

  const kitchen = await pageAs(browser);
  await kitchen.goto('/kitchen');
  const t = ticket(kitchen, token);
  await expect(t.getByText(`1 x Dal Makhani ${id}`)).toBeVisible({ timeout: 10_000 });
  await kitchen.getByLabel('Station').selectOption({ label: `Tandoor ${id}` });
  await expect(t.getByText(`1 x Paneer Tikka ${id}`)).toBeVisible();
  await expect(t.getByText(`1 x Dal Makhani ${id}`)).toHaveCount(0);
  await kitchen.getByLabel('Station').selectOption({ index: 0 });
});

test('B3 time-based menu: a category outside its hours is hidden on the POS', async ({ page }) => {
  const api = await ownerApi();
  const id = uid();
  const closed = await addCategory(api, `Breakfast ${id}`, { availableFrom: istTime(6), availableTo: istTime(6, 30) });
  await addItem(api, { name: `Poha ${id}`, price: 50, categoryId: closed.id });

  await signIn(page);
  await page.goto('/pos');
  await expect(page.getByRole('button', { name: 'Add Fries', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: `Add Poha ${id}`, exact: true })).toHaveCount(0);

  await ok(await api.put(`/api/admin/menu/categories/${closed.id}`, {
    data: { name: `Breakfast ${id}`, sortOrder: 50, isActive: true, availableFrom: null, availableTo: null },
  }));
  await page.reload();
  await expect(page.getByRole('button', { name: `Add Poha ${id}`, exact: true })).toBeVisible();
});

test('B4 combo: the POS tile lists the parts and the kitchen sees every part', async ({ browser }) => {
  const api = await ownerApi();
  const id = uid();
  const category = await addCategory(api, `Thalis ${id}`);
  const dal = await addItem(api, { name: `Dal ${id}`, price: 180, categoryId: category.id });
  const naan = await addItem(api, { name: `Naan ${id}`, price: 40, categoryId: category.id });
  const combo = await addItem(api, { name: `Thali Combo ${id}`, price: 299, categoryId: category.id });
  await ok(await api.put(`/api/admin/menu/items/${combo.id}/combo`, {
    data: { parts: [{ partItemId: dal.id, quantity: 1 }, { partItemId: naan.id, quantity: 2 }] },
  }));

  const pos = await pageAs(browser);
  await pos.goto('/pos');
  await expect(pos.getByText(`1 x Dal ${id} · 2 x Naan ${id}`)).toBeVisible();
  const token = await placeTakeaway(pos, `Meena ${id}`, [`Thali Combo ${id}`]);

  const kitchen = await pageAs(browser);
  await kitchen.goto('/kitchen');
  const t = ticket(kitchen, token);
  await expect(t.getByText(`1 x Thali Combo ${id}`)).toBeVisible({ timeout: 10_000 });
  await expect(t.getByText(`= 1 x Dal ${id}, 2 x Naan ${id}`)).toBeVisible();
});
