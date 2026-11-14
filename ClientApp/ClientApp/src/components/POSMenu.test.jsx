// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, act } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import POSMenu from './POSMenu';
import { handlers } from '../test/signalr-mock';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

const MENU = [
  { id: 1, name: 'Classic Burger', price: 6, category: 'Mains', isVeg: false },
  { id: 3, name: 'Fries', price: 3, category: 'Sides', isVeg: true },
];
const TABLES = [{ id: 1, tableNumber: '1' }];
const EDIT_ORDER = {
  id: 42, tokenNumber: 9, status: 'Received', version: 3, orderType: 'DineIn', tableId: 1,
  items: [{ id: 1, menuItemId: 3, itemName: 'Fries', unitPrice: 3, quantity: 1, note: 'Jain' }],
};

function mockApi({ orderStatus = 'Received', saveStatus = 200 } = {}) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/menuitems') return { ok: true, json: async () => MENU };
    if (url === '/api/tables') return { ok: true, json: async () => TABLES };
    if (url === '/api/orders') return { ok: true, status: 200, json: async () => ({ message: 'Order placed', orderId: 1, tokenNumber: 7 }) };
    if (url === '/api/orders/42' && !options?.method) return { ok: true, json: async () => ({ ...EDIT_ORDER, status: orderStatus }) };
    if (url === '/api/orders/42/items') return { ok: saveStatus === 200, status: saveStatus, json: async () => ({ code: saveStatus === 409 ? 'conflict' : undefined }) };
    return { ok: false, status: 404, json: async () => ({}) };
  }));
  return calls;
}

function renderPos(path = '/pos') {
  render(
    <MemoryRouter initialEntries={[path]}>
      <I18nProvider>
        <ToastProvider>
          <Routes>
            <Route path="/pos" element={<POSMenu />} />
            <Route path="/orders" element={<p>Orders page</p>} />
          </Routes>
        </ToastProvider>
      </I18nProvider>
    </MemoryRouter>,
  );
}

const lastPost = (calls) => JSON.parse(calls.filter((c) => c.options?.method === 'POST').at(-1).options.body);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('POS screen', () => {
  it('builds a dine-in order with a note and places it', async () => {
    const calls = mockApi();
    renderPos();

    const burger = await screen.findByRole('button', { name: 'Add Classic Burger' });
    fireEvent.click(burger);
    fireEvent.click(burger);
    fireEvent.click(screen.getByRole('button', { name: 'Add Fries' }));
    expect(screen.getByText('Total, 3 items')).toBeInTheDocument();
    expect(screen.getByText('₹15.00')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Add note for Fries' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Add note for Fries' }), { target: { value: 'Jain' } });
    fireEvent.change(screen.getByLabelText('Table'), { target: { value: '1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));

    expect(await screen.findByText('Order placed. Token 7')).toBeInTheDocument();
    const body = lastPost(calls);
    expect(body.orderType).toBe('DineIn');
    expect(body.table).toBe(1);
    expect(body.items).toEqual([{ menuItemId: 1, quantity: 2, note: null }, { menuItemId: 3, quantity: 1, note: 'Jain' }]);
    expect(body.clientOrderId).toBeTruthy();
    expect(screen.getByText('Tap an item to add it to the order.')).toBeInTheDocument();
  });

  it('marks veg and non-veg items', async () => {
    mockApi();
    renderPos();
    await screen.findByRole('button', { name: 'Add Fries' });
    expect(screen.getAllByRole('img', { name: 'Veg' })).toHaveLength(1);
    expect(screen.getAllByRole('img', { name: 'Non-veg' })).toHaveLength(1);
  });

  it('asks for a table before sending a dine-in order', async () => {
    const calls = mockApi();
    renderPos();
    fireEvent.click(await screen.findByRole('button', { name: 'Add Fries' }));
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));

    expect(await screen.findByText('Choose a table first.')).toBeInTheDocument();
    expect(calls.some((c) => c.options?.method === 'POST')).toBe(false);
  });

  it('takeaway needs a customer name instead of a table', async () => {
    const calls = mockApi();
    renderPos();
    fireEvent.click(await screen.findByRole('button', { name: 'Takeaway' }));
    expect(screen.queryByLabelText('Table')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add Fries' }));
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));
    expect(await screen.findByText("Enter the customer's name.")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Customer name'), { target: { value: 'Ravi' } });
    fireEvent.change(screen.getByLabelText('Phone (optional)'), { target: { value: '98250 12345' } });
    expect(screen.getByText('Takeaway · Ravi')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));

    expect(await screen.findByText('Order placed. Token 7')).toBeInTheDocument();
    const body = lastPost(calls);
    expect(body).toMatchObject({ orderType: 'Takeaway', table: null, customerName: 'Ravi', customerPhone: '98250 12345' });
  });

  it('changes quantity and clears after confirming', async () => {
    mockApi();
    renderPos();
    fireEvent.click(await screen.findByRole('button', { name: 'Add Fries' }));
    fireEvent.click(screen.getByRole('button', { name: 'One more Fries' }));
    expect(screen.getByText('Total, 2 items')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'One less Fries' }));
    expect(screen.getByText('Total, 1 items')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Clear order' }));
    fireEvent.click(screen.getByRole('button', { name: 'Yes, clear' }));
    expect(screen.getByText('Tap an item to add it to the order.')).toBeInTheDocument();
  });

  it('edits an order before cooking and saves with its version', async () => {
    const calls = mockApi();
    renderPos('/pos?edit=42');

    expect(await screen.findByText('Editing token 9')).toBeInTheDocument();
    expect(await screen.findByDisplayValue('Jain')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'One more Fries' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

    expect(await screen.findByText('Orders page')).toBeInTheDocument();
    const post = calls.find((c) => c.url === '/api/orders/42/items');
    expect(JSON.parse(post.options.body)).toEqual({ items: [{ menuItemId: 3, quantity: 2, note: 'Jain' }], version: 3 });
  });

  it('says so when another screen changed the order', async () => {
    mockApi({ saveStatus: 409 });
    renderPos('/pos?edit=42');
    await screen.findByText('Editing token 9');
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('This order was changed on another screen. Open it again.')).toBeInTheDocument();
  });

  it('refuses to edit an order that is already cooking', async () => {
    mockApi({ orderStatus: 'Preparing' });
    renderPos('/pos?edit=42');
    expect(await screen.findByText('Orders page')).toBeInTheDocument();
    expect(screen.getByText("Cooking has started, so this order can't be changed.")).toBeInTheDocument();
  });

  it('shows a toast when the kitchen marks an order ready', async () => {
    mockApi();
    renderPos();
    await screen.findByRole('button', { name: 'Add Fries' });
    act(() => { handlers.OrderStatusChanged({ orderId: 5, tokenNumber: 12, status: 'Ready' }); });
    expect(screen.getByText('Order 12 is ready')).toBeInTheDocument();
  });
});
