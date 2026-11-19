// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import OrderHistory from './OrderHistory';

const ORDERS = [
  { id: 1, tokenNumber: 1, tableId: 2, orderType: 'DineIn', status: 'Ready', version: 3, createdAt: '2026-11-14T07:00:00Z', items: [{ quantity: 2, itemName: 'Classic Burger', unitPrice: 6 }] },
  { id: 2, tokenNumber: 2, orderType: 'Takeaway', customerName: 'Ravi', status: 'Received', version: 1, createdAt: '2026-11-14T08:00:00Z', items: [{ quantity: 1, itemName: 'Fries', unitPrice: 3, note: 'Jain' }] },
];

function mockApi(cancelStatus = 200) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (options?.method === 'POST') return { ok: cancelStatus === 200, status: cancelStatus, json: async () => ({ code: 'cooking-started' }) };
    return { ok: true, json: async () => ORDERS };
  }));
  return calls;
}

function renderPage() {
  render(<MemoryRouter><I18nProvider><ToastProvider><OrderHistory /></ToastProvider></I18nProvider></MemoryRouter>);
}

function mockMove() {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/tables') {
      return { ok: true, json: async () => [
        { id: 1, tableNumber: '1', status: 'Occupied' }, { id: 2, tableNumber: '2', status: 'Free' }, { id: 3, tableNumber: '3', status: 'Billed' }] };
    }
    if (url === '/api/orders/50/move') return { ok: true, status: 200, json: async () => ({ id: 50, table: { id: 2, tableNumber: '2' } }) };
    return { ok: true, json: async () => [{ id: 50, tokenNumber: 21, orderType: 'DineIn', tableId: 1, table: { id: 1, tableNumber: '1' },
      status: 'Preparing', version: 6, createdAt: '2026-11-14T07:00:00Z', items: [] }] };
  }));
  return calls;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Order history', () => {
  it('splits some items off to a new order', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      calls.push({ url, options });
      if (url === '/api/orders/60/split') return { ok: true, status: 200, json: async () => ({ id: 61, tokenNumber: 33 }) };
      return { ok: true, json: async () => [{ id: 60, tokenNumber: 30, orderType: 'DineIn', tableId: 1, table: { id: 1, tableNumber: '1' }, status: 'Ready', version: 2,
        createdAt: '2026-11-14T07:00:00Z', items: [{ id: 5, itemName: 'Butter Naan', quantity: 3, unitPrice: 40 }, { id: 6, itemName: 'Tea', quantity: 1, unitPrice: 20 }] }] };
    }));
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Split bill' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('3 x Butter Naan'), { target: { value: '1' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Split' }));

    expect(await screen.findByText('Split off as token 33. Bill it from this list.')).toBeInTheDocument();
    expect(JSON.parse(calls.find((c) => c.url === '/api/orders/60/split').options.body)).toEqual({ lines: [{ orderItemId: 5, quantity: 1 }], version: 2 });
  });

  it('merges another table into this order', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      calls.push({ url, options });
      if (url === '/api/orders/70/merge') return { ok: true, status: 200, json: async () => ({ id: 70, tokenNumber: 30 }) };
      return { ok: true, json: async () => [
        { id: 70, tokenNumber: 30, orderType: 'DineIn', tableId: 1, table: { id: 1, tableNumber: '1' }, status: 'Preparing', version: 4, createdAt: '2026-11-14T09:00:00Z', items: [] },
        { id: 71, tokenNumber: 31, orderType: 'DineIn', tableId: 2, table: { id: 2, tableNumber: '2' }, status: 'Received', version: 2, createdAt: '2026-11-14T08:00:00Z', items: [] },
      ] };
    }));
    renderPage();
    fireEvent.click((await screen.findAllByRole('button', { name: 'Merge table' }))[0]);
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Merge in'), { target: { value: '71' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Merge' }));

    expect(await screen.findByText('Token 31 merged into token 30.')).toBeInTheDocument();
    expect(JSON.parse(calls.find((c) => c.url === '/api/orders/70/merge').options.body)).toEqual({ fromOrderId: 71, version: 4, fromVersion: 2 });
  });

  it('moves an open dine-in order to a free table', async () => {
    const calls = mockMove();
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Move table' }));
    const dialog = await screen.findByRole('dialog');
    const select = within(dialog).getByLabelText('Move to');
    await vi.waitFor(() => expect(within(select).getAllByRole('option')).toHaveLength(2));
    fireEvent.change(select, { target: { value: '2' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Move' }));

    expect(await screen.findByText('Token 21 moved to Table 2.')).toBeInTheDocument();
    const post = calls.find((c) => c.url === '/api/orders/50/move');
    expect(JSON.parse(post.options.body)).toEqual({ tableId: 2, version: 6 });
  });

  it('lists orders newest first with type, notes and totals', async () => {
    mockApi();
    renderPage();
    const rows = await screen.findAllByRole('row');
    expect(within(rows[1]).getByText('1 x Fries (Jain)')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Takeaway · Ravi')).toBeInTheDocument();
    expect(within(rows[2]).getByText('2 x Classic Burger')).toBeInTheDocument();
    expect(within(rows[2]).getByText('₹12.00')).toBeInTheDocument();
    expect(within(rows[2]).getByText('Table 2')).toBeInTheDocument();
  });

  it('closes a ready dine-in order with Served', async () => {
    const calls = mockApi();
    renderPage();
    const row = (await screen.findByText('Classic Burger', { exact: false })).closest('tr');
    fireEvent.click(within(row).getByRole('button', { name: 'Served' }));
    expect(await screen.findByText('Token 1 done.')).toBeInTheDocument();
    const sent = calls.find((c) => c.url === '/api/orders/1/status');
    expect(JSON.parse(sent.options.body)).toEqual({ status: 'Completed', version: 3 });
  });

  it('offers edit and cancel only before cooking', async () => {
    mockApi();
    renderPage();
    const rows = await screen.findAllByRole('row');
    expect(within(rows[1]).getByRole('link', { name: 'Edit' })).toHaveAttribute('href', '/pos?edit=2');
    expect(within(rows[2]).queryByRole('button', { name: 'Cancel order' })).not.toBeInTheDocument();
  });

  it('cancels with a reason and the order version', async () => {
    const calls = mockApi();
    renderPage();
    const rows = await screen.findAllByRole('row');
    fireEvent.click(within(rows[1]).getByRole('button', { name: 'Cancel order' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes, cancel' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Choose a reason.');

    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'reasonCustomerLeft' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes, cancel' }));

    expect(await screen.findByText('Token 2 cancelled.')).toBeInTheDocument();
    const post = calls.find((c) => c.options?.method === 'POST');
    expect(post.url).toBe('/api/orders/2/cancel');
    expect(JSON.parse(post.options.body)).toEqual({ reason: 'Customer left', version: 1 });
  });

  it('explains when cooking already started', async () => {
    mockApi(409);
    renderPage();
    const rows = await screen.findAllByRole('row');
    fireEvent.click(within(rows[1]).getByRole('button', { name: 'Cancel order' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'reasonWrongOrder' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes, cancel' }));
    expect(await within(dialog).findByText("Cooking has started, so this order can't be changed.")).toBeInTheDocument();
  });

  it('loads another day and shows an error with retry', async () => {
    const fetchMock = vi.fn(async () => ({ ok: false, json: async () => ({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));
    fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-11-13' } });
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/orders?date=2026-11-13'));
  });
});
