// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
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

describe('POS after placing an order', () => {
  it('offers Bill & pay and Print KOT for the order just placed', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      if (url === '/api/menuitems') return { ok: true, json: async () => [{ id: 3, name: 'Fries', price: 90, category: 'Sides', isVeg: true }] };
      if (url === '/api/tables') return { ok: true, json: async () => [] };
      if (url === '/api/store/settings') return { ok: true, json: async () => COUNTER };
      if (url === '/api/orders') return { ok: true, status: 200, json: async () => ({ orderId: 21, tokenNumber: 4 }) };
      if (url === '/api/print-settings') return { ok: true, status: 200, json: async () => ({ upiReady: false }) };
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    const open = vi.fn();
    vi.stubGlobal('open', open);
    render(<MemoryRouter initialEntries={['/pos']}><I18nProvider><ToastProvider><POSMenu /></ToastProvider></I18nProvider></MemoryRouter>);

    expect(screen.queryByRole('button', { name: 'Bill & pay' })).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: 'Add Fries' }));
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));

    expect(await screen.findByText('Token 4 placed')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Print KOT' }));
    expect(open).toHaveBeenCalledWith('/print/kot/21?auto=1', '_blank');

    fireEvent.click(screen.getByRole('button', { name: 'Bill & pay' }));
    expect(await screen.findByRole('button', { name: 'Make bill' })).toBeInTheDocument();
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('button', { name: 'Make bill' })).not.toBeInTheDocument();
  });
});
