// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import GuestOrderPage from './GuestOrderPage';

const MENU = {
  restaurantName: 'Anand Thali', outletName: 'Main', tableNumber: '5', ordering: true,
  items: [{ id: 1, name: 'Masala Tea', price: 20, category: 'Drinks', categoryId: 1, description: null, isVeg: true, variants: [], addOns: [] }],
};

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/t/abc123']}>
      <I18nProvider><Routes><Route path="/t/:token" element={<GuestOrderPage />} /></Routes></I18nProvider>
    </MemoryRouter>,
  );
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Guest QR page', () => {
  it('lets a guest order from the table and call a waiter', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
      calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
      if (url === '/api/qr/abc123/order') return { ok: true, status: 200, json: async () => ({ tokenNumber: 12, round: 1 }) };
      if (url === '/api/qr/abc123/call') return { ok: true, status: 200, json: async () => ({ message: 'A waiter is on the way.' }) };
      return { ok: true, status: 200, json: async () => MENU };
    }));
    renderPage();

    expect(await screen.findByText('Table 5')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add Masala Tea' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add Masala Tea' }));
    expect(screen.getByText('Total ₹40.00')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Send order' }));

    expect(await screen.findByText('Order sent. Your token is 12.')).toBeInTheDocument();
    const post = calls.find((c) => c.url === '/api/qr/abc123/order');
    expect(post.body.items).toEqual([{ menuItemId: 1, quantity: 2 }]);
    expect(post.body.clientOrderId).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Call waiter' }));
    expect(await screen.findByText('A waiter is on the way.')).toBeInTheDocument();
  });

  it('has category buttons and a dish search', async () => {
    const menu = {
      ...MENU,
      items: [
        ...MENU.items,
        { id: 2, name: 'Gujarati Thali', price: 220, category: 'Thali', categoryId: 2, description: null, isVeg: true, variants: [], addOns: [] },
      ],
    };
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => menu })));
    renderPage();
    expect(await screen.findByText('Table 5')).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: 'Menu sections' });
    expect(within(nav).getByRole('button', { name: 'Thali' })).toBeInTheDocument();
    fireEvent.click(within(nav).getByRole('button', { name: 'Thali' }));

    fireEvent.change(screen.getByLabelText('Search dishes'), { target: { value: 'thali' } });
    expect(screen.getByRole('button', { name: 'Add Gujarati Thali' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add Masala Tea' })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Search dishes'), { target: { value: 'pizza' } });
    expect(screen.getByText('No dish matches. Try another word.')).toBeInTheDocument();
  });

  it('shows the menu only when ordering by QR is off, and a clear note for unknown codes', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => ({ ...MENU, ordering: false }) })));
    renderPage();
    expect(await screen.findByText('Ordering from the table is off here. Please call the staff.')).toBeInTheDocument();
    expect(screen.getByText('Masala Tea')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add Masala Tea' })).not.toBeInTheDocument();
    cleanup();

    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: false, status: 404, json: async () => ({}) })));
    renderPage();
    expect(await screen.findByText('This QR code is not in use. Please ask the staff.')).toBeInTheDocument();
  });
});
