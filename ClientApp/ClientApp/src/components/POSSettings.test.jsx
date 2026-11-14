// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import POSMenu from './POSMenu';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

const COUNTER = {
  timeZone: 'Asia/Kolkata', businessDayStartHour: 4, dineInEnabled: false, takeawayEnabled: true, defaultOrderType: 'Takeaway',
  takeawayNeedsName: false, maxItemQuantity: 99, firstToken: 1, kitchenScreenEnabled: true,
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

// The POS follows the outlet settings: only enabled order types, starting with the default one.
describe('POS with outlet settings', () => {
  it('a takeaway counter shows no dine-in and needs no name', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      calls.push({ url, options });
      if (url === '/api/menuitems') return { ok: true, json: async () => [{ id: 3, name: 'Fries', price: 3, category: 'Sides', isVeg: true }] };
      if (url === '/api/tables') return { ok: true, json: async () => [{ id: 1, tableNumber: '1' }] };
      if (url === '/api/store/settings') return { ok: true, json: async () => COUNTER };
      if (url === '/api/orders') return { ok: true, status: 200, json: async () => ({ orderId: 1, tokenNumber: 4 }) };
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    render(<MemoryRouter initialEntries={['/pos']}><I18nProvider><ToastProvider><POSMenu /></ToastProvider></I18nProvider></MemoryRouter>);

    fireEvent.click(await screen.findByRole('button', { name: 'Add Fries' }));
    expect(screen.queryByRole('button', { name: 'Dine-in' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Takeaway' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByLabelText('Table')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));
    expect(await screen.findByText('Order placed. Token 4')).toBeInTheDocument();
    const body = JSON.parse(calls.find((c) => c.options?.method === 'POST').options.body);
    expect(body).toMatchObject({ orderType: 'Takeaway', customerName: null });
  });
});
