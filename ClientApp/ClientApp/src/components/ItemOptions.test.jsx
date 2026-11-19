// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import POSMenu from './POSMenu';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

const PIZZA = {
  id: 5, name: 'Pizza', price: 250, category: 'Mains', isVeg: true,
  variants: [{ id: 51, name: 'Regular', price: 250, isAvailable: true }, { id: 52, name: 'Large', price: 350, isAvailable: true }],
  addOns: [{ id: 61, name: 'Extra cheese', price: 30, isAvailable: true }, { id: 62, name: 'Olives', price: 20, isAvailable: false }],
};
const TEA = { id: 7, name: 'Tea', price: 20, category: 'Drinks', isVeg: true, variants: [], addOns: [] };

function mockApi() {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/menuitems') return { ok: true, json: async () => [PIZZA, TEA] };
    if (url === '/api/tables') return { ok: true, json: async () => [{ id: 1, tableNumber: '1' }] };
    if (url === '/api/orders') return { ok: true, status: 200, json: async () => ({ message: 'Order placed', orderId: 1, tokenNumber: 7 }) };
    return { ok: false, status: 404, json: async () => ({}) };
  }));
  return calls;
}

const renderPos = () => render(
  <MemoryRouter initialEntries={['/pos']}>
    <I18nProvider>
      <ToastProvider>
        <Routes><Route path="/pos" element={<POSMenu />} /></Routes>
      </ToastProvider>
    </I18nProvider>
  </MemoryRouter>,
);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('POS sizes and extras', () => {
  it('asks for the size and extras, and sends them with the order', async () => {
    const calls = mockApi();
    renderPos();
    expect(await screen.findByText('from ₹250.00')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Add Pizza' }));
    expect(screen.getByRole('dialog', { name: 'Pizza' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Olives/ })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: /Large/ }));
    fireEvent.click(screen.getByRole('button', { name: /Extra cheese/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Add · ₹380.00' }));

    expect(screen.getByText('Pizza (Large)')).toBeInTheDocument();
    expect(screen.getByText('Extra cheese')).toBeInTheDocument();
    expect(screen.getAllByText('₹380.00').length).toBeGreaterThan(0);

    // A plain item still goes straight into the order.
    fireEvent.click(screen.getByRole('button', { name: 'Add Tea' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Table'));
    fireEvent.click(within(screen.getByRole('dialog', { name: 'Tables' })).getByRole('button', { name: 'Table 1' }));
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));
    expect(await screen.findByText('Order placed. Token 7')).toBeInTheDocument();
    const body = JSON.parse(calls.filter((c) => c.options?.method === 'POST').at(-1).options.body);
    expect(body.items).toEqual([
      { menuItemId: 5, quantity: 1, note: null, variantId: 52, addOnIds: [61] },
      { menuItemId: 7, quantity: 1, note: null },
    ]);
  });

  it('keeps different sizes of one item as separate lines', async () => {
    mockApi();
    renderPos();
    fireEvent.click(await screen.findByRole('button', { name: 'Add Pizza' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add · ₹250.00' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add Pizza' }));
    fireEvent.click(screen.getByRole('button', { name: /Large/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Add · ₹350.00' }));
    expect(screen.getByText('Pizza (Regular)')).toBeInTheDocument();
    expect(screen.getByText('Pizza (Large)')).toBeInTheDocument();
    expect(screen.getByText('Total, 2 items')).toBeInTheDocument();
  });
});
