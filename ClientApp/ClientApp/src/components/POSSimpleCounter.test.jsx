// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import POSMenu from './POSMenu';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

const BAKERY = {
  timeZone: 'Asia/Kolkata', businessDayStartHour: 4, dineInEnabled: true, takeawayEnabled: true, defaultOrderType: 'Takeaway',
  takeawayNeedsName: false, maxItemQuantity: 99, firstToken: 1, kitchenScreenEnabled: true, simpleCounter: true,
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

// Bakery: no table, no name; placing the order opens the bill straight away, then the POS is ready for the next guest.
describe('POS as a simple counter (bakery)', () => {
  it('takes a dine-in order without a table and opens the bill', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      calls.push({ url, options });
      if (url === '/api/menuitems') return { ok: true, json: async () => [{ id: 3, name: 'Puff', price: 20, category: 'Bakery', isVeg: true }] };
      if (url === '/api/tables') return { ok: true, json: async () => [{ id: 1, tableNumber: '1' }] };
      if (url === '/api/store/settings') return { ok: true, json: async () => BAKERY };
      if (url === '/api/orders') return { ok: true, status: 200, json: async () => ({ orderId: 7, tokenNumber: 12 }) };
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    render(<MemoryRouter initialEntries={['/pos']}><I18nProvider><ToastProvider><POSMenu /></ToastProvider></I18nProvider></MemoryRouter>);

    fireEvent.click(await screen.findByRole('button', { name: 'Add Puff' }));
    fireEvent.click(screen.getByRole('button', { name: 'Dine-in' }));
    expect(screen.queryByLabelText('Table')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));
    expect(await screen.findByText('Order placed. Token 12')).toBeInTheDocument();
    const body = JSON.parse(calls.find((c) => c.options?.method === 'POST').options.body);
    expect(body).toMatchObject({ orderType: 'DineIn', table: null, customerName: null });
    await waitFor(() => expect(calls.some((c) => c.url === '/api/orders/7/bill')).toBe(true));
  });
});
