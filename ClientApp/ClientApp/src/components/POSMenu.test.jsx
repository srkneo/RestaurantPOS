// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, act } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import POSMenu from './POSMenu';
import { handlers } from '../test/signalr-mock';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

const MENU = [
  { id: 1, name: 'Classic Burger', price: 6, category: 'Mains' },
  { id: 3, name: 'Fries', price: 3, category: 'Sides' },
];
const TABLES = [{ id: 1, tableNumber: '1' }];

function mockApi() {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/menuitems') return { ok: true, json: async () => MENU };
    if (url === '/api/tables') return { ok: true, json: async () => TABLES };
    if (url === '/api/orders') return { ok: true, json: async () => ({ message: 'Order placed', orderId: 1, tokenNumber: 7 }) };
    return { ok: false, json: async () => ({}) };
  }));
  return calls;
}

function renderPos() {
  render(<I18nProvider><ToastProvider><POSMenu /></ToastProvider></I18nProvider>);
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('POS screen', () => {
  it('builds an order and places it', async () => {
    const calls = mockApi();
    renderPos();

    const burger = await screen.findByRole('button', { name: 'Add Classic Burger' });
    fireEvent.click(burger);
    fireEvent.click(burger);
    fireEvent.click(screen.getByRole('button', { name: 'Add Fries' }));
    expect(screen.getByText('Total, 3 items')).toBeInTheDocument();
    expect(screen.getByText('₹15.00')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Table'), { target: { value: '1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));

    expect(await screen.findByText('Order placed. Token 7')).toBeInTheDocument();
    const post = calls.find((c) => c.options?.method === 'POST');
    const body = JSON.parse(post.options.body);
    expect(body.table).toBe(1);
    expect(body.items).toEqual([{ menuItemId: 1, quantity: 2 }, { menuItemId: 3, quantity: 1 }]);
    expect(body.clientOrderId).toBeTruthy();
    expect(screen.getByText('Tap an item to add it to the order.')).toBeInTheDocument();
  });

  it('asks for a table before sending', async () => {
    const calls = mockApi();
    renderPos();
    fireEvent.click(await screen.findByRole('button', { name: 'Add Fries' }));
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));

    expect(await screen.findByText('Choose a table first.')).toBeInTheDocument();
    expect(calls.some((c) => c.options?.method === 'POST')).toBe(false);
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

  it('shows a toast when the kitchen marks an order ready', async () => {
    mockApi();
    renderPos();
    await screen.findByRole('button', { name: 'Add Fries' });
    act(() => { handlers.OrderStatusChanged({ orderId: 5, tokenNumber: 12, status: 'Ready' }); });
    expect(screen.getByText('Order 12 is ready')).toBeInTheDocument();
  });
});
