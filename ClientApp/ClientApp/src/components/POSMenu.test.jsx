// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, act, within } from '@testing-library/react';
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
  it('shows what a combo includes under its name', async () => {
    mockApi();
    const base = globalThis.fetch;
    vi.stubGlobal('fetch', vi.fn(async (url, options) => (url === '/api/menuitems'
      ? { ok: true, json: async () => [...MENU, { id: 9, name: 'Thali Combo', price: 299, category: 'Mains', isVeg: true, includes: '1 x Dal, 2 x Naan' }] }
      : base(url, options))));
    renderPos();
    expect(await screen.findByText('1 x Dal · 2 x Naan')).toBeInTheDocument();
  });

  it('shows a bar with the total that jumps to the order on small screens', async () => {
    mockApi();
    renderPos();
    const scrollIntoView = vi.fn();
    window.HTMLElement.prototype.scrollIntoView = scrollIntoView;

    const burger = await screen.findByRole('button', { name: 'Add Classic Burger' });
    expect(screen.queryByRole('button', { name: 'Review order' })).not.toBeInTheDocument();
    fireEvent.click(burger);
    fireEvent.click(screen.getByRole('button', { name: 'Add Fries' }));

    const bar = screen.getByRole('region', { name: 'Order so far' });
    expect(bar).toHaveTextContent('2 items · ₹9.00');
    fireEvent.click(screen.getByRole('button', { name: 'Review order' }));
    expect(scrollIntoView).toHaveBeenCalled();
  });

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
    fireEvent.click(screen.getByLabelText('Table'));
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Tables' })).getByRole('button', { name: 'Table 1' }));
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

  it('adds to the open order of an occupied table as a new round', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      calls.push({ url, options });
      if (url === '/api/menuitems') return { ok: true, json: async () => MENU };
      if (url === '/api/tables') return { ok: true, json: async () => [{ id: 1, tableNumber: '1', status: 'Occupied', orderId: 42, tokenNumber: 9, orderVersion: 4 }] };
      if (url === '/api/orders/42/add-items') {
        return { ok: true, status: 200, json: async () => ({ id: 42, tokenNumber: 9, version: 5, items: [{ id: 1, round: 1 }, { id: 2, round: 2 }] }) };
      }
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);
    renderPos();
    fireEvent.click(await screen.findByRole('button', { name: 'Add Fries' }));
    fireEvent.click(screen.getByLabelText('Table'));
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Tables' })).getByRole('button', { name: 'Table 1' }));
    expect(screen.getByText('Table 1 has token 9 open. New items are added to it.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add to token 9' }));

    expect(await screen.findByText('Added to token 9.')).toBeInTheDocument();
    const post = calls.find((c) => c.url === '/api/orders/42/add-items');
    expect(JSON.parse(post.options.body)).toEqual({ items: [{ menuItemId: 3, quantity: 1, note: null }], version: 4 });
    fireEvent.click(screen.getByRole('button', { name: 'Print KOT' }));
    expect(open).toHaveBeenCalledWith('/print/kot/42?auto=1&round=2', '_blank', 'noopener');
    open.mockRestore();
  });

  it('picks a table from the table grid', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      if (url === '/api/menuitems') return { ok: true, json: async () => MENU };
      if (url === '/api/tables') {
        return { ok: true, json: async () => [
          { id: 1, tableNumber: '1', status: 'Occupied', orderId: 42, tokenNumber: 9, orderVersion: 4, since: new Date(Date.now() - 20 * 60000).toISOString() },
          { id: 2, tableNumber: '2', status: 'Free' },
          { id: 3, tableNumber: '3', status: 'Billed', orderId: 43, tokenNumber: 10 },
        ] };
      }
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    renderPos();
    fireEvent.click(await screen.findByLabelText('Table'));
    const grid = screen.getByRole('dialog', { name: 'Tables' });
    expect(within(grid).getByText('Token 9 · 20 min')).toBeInTheDocument();
    expect(within(grid).getByText('Bill printed')).toBeInTheDocument();
    fireEvent.click(within(grid).getByRole('button', { name: 'Table 1' }));

    expect(screen.queryByRole('dialog', { name: 'Tables' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Table')).toHaveTextContent('Table 1');
    expect(screen.getByText('Table 1 has token 9 open. New items are added to it.')).toBeInTheDocument();
  });

  it('shows a toast when the kitchen marks an order ready', async () => {
    mockApi();
    renderPos();
    await screen.findByRole('button', { name: 'Add Fries' });
    act(() => { handlers.OrderStatusChanged({ orderId: 5, tokenNumber: 12, status: 'Ready' }); });
    expect(screen.getByText('Order 12 is ready')).toBeInTheDocument();
  });
});
