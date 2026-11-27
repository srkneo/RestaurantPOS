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

const DAIRY = {
  timeZone: 'Asia/Kolkata', businessDayStartHour: 4, dineInEnabled: false, takeawayEnabled: true, defaultOrderType: 'Takeaway',
  takeawayNeedsName: false, maxItemQuantity: 99, firstToken: 1, kitchenScreenEnabled: true, simpleCounter: true,
};
const MENU = [
  { id: 5, name: 'Chaas', price: 60, category: 'Dairy', isVeg: true, sellUnit: 'litre' },
  { id: 6, name: 'Lassi', price: 30, category: 'Dairy', isVeg: true, sellUnit: 'piece' },
];

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const start = () => {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/menuitems') return { ok: true, json: async () => MENU };
    if (url === '/api/tables') return { ok: true, json: async () => [] };
    if (url === '/api/store/settings') return { ok: true, json: async () => DAIRY };
    if (url === '/api/orders') return { ok: true, status: 200, json: async () => ({ orderId: 7, tokenNumber: 3 }) };
    return { ok: false, status: 404, json: async () => ({}) };
  }));
  render(<MemoryRouter initialEntries={['/pos']}><I18nProvider><ToastProvider><POSMenu /></ToastProvider></I18nProvider></MemoryRouter>);
  return calls;
};

// Dairy: chaas is sold by the litre, so the POS asks how much and sends the measure with the line.
describe('POS sell by weight (dairy)', () => {
  it('asks how much and sends the measure', async () => {
    const calls = start();
    fireEvent.click(await screen.findByRole('button', { name: 'Add Chaas' }));
    expect(screen.getByRole('dialog', { name: 'Chaas' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '500 ml' }));
    expect(screen.queryByRole('dialog', { name: 'Chaas' })).not.toBeInTheDocument();
    expect(await screen.findByText('Chaas (500 ml)')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));
    await waitFor(() => expect(calls.some((c) => c.url === '/api/orders' && c.options?.method === 'POST')).toBe(true));
    const body = JSON.parse(calls.find((c) => c.url === '/api/orders' && c.options?.method === 'POST').options.body);
    expect(body.items).toHaveLength(1);
    expect(body.items[0]).toMatchObject({ menuItemId: 5, quantity: 1, measure: 0.5 });
  });

  it('adds an item sold by piece straight away', async () => {
    const calls = start();
    fireEvent.click(await screen.findByRole('button', { name: 'Add Lassi' }));
    expect(screen.queryByRole('dialog', { name: 'Lassi' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));
    await waitFor(() => expect(calls.some((c) => c.url === '/api/orders' && c.options?.method === 'POST')).toBe(true));
    const body = JSON.parse(calls.find((c) => c.url === '/api/orders' && c.options?.method === 'POST').options.body);
    expect(body.items[0]).toMatchObject({ menuItemId: 6, quantity: 1 });
    expect(body.items[0].measure).toBeUndefined();
  });
});
