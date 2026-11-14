// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import MenuPage from './MenuPage';
import { MemoryRouter } from 'react-router-dom';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

const CATEGORIES = [{ id: 1, name: 'Snacks', sortOrder: 1, isActive: true }];
const ITEMS = [
  { id: 10, name: 'Samosa', price: 15, categoryId: 1, category: 'Snacks', description: null, isVeg: true, isAvailable: true, taxRate: 5, sortOrder: 1 },
  { id: 11, name: 'Chicken Roll', price: 90, categoryId: 1, category: 'Snacks', description: null, isVeg: false, isAvailable: false, taxRate: 5, sortOrder: 2 },
];

function mockApi(writeResponse = { ok: true, status: 200, json: async () => ({}) }) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (options?.method) return writeResponse;
    if (url.endsWith('/categories')) return { ok: true, json: async () => CATEGORIES };
    return { ok: true, json: async () => ITEMS };
  }));
  return calls;
}

function renderPage() {
  render(<MemoryRouter><I18nProvider><ToastProvider><MenuPage /></ToastProvider></I18nProvider></MemoryRouter>);
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Menu page', () => {
  it('lists items with price, GST and availability', async () => {
    mockApi();
    renderPage();
    const row = (await screen.findByText('Chicken Roll')).closest('tr');
    expect(within(row).getByText('₹90.00')).toBeInTheDocument();
    expect(within(row).getByText('Sold out')).toBeInTheDocument();
    expect(within(row).getByRole('img', { name: 'Non-veg' })).toBeInTheDocument();
  });

  it('marks an item sold out with one tap', async () => {
    const calls = mockApi();
    renderPage();
    fireEvent.click(await screen.findByRole('switch', { name: 'Samosa: Available' }));
    expect(await screen.findByText('Samosa: Sold out')).toBeInTheDocument();
    const put = calls.find((c) => c.options?.method === 'PUT');
    expect(put.url).toBe('/api/admin/menu/items/10/availability');
    expect(JSON.parse(put.options.body)).toEqual({ isAvailable: false });
  });

  it('adds an item after checking the fields', async () => {
    const calls = mockApi({ ok: true, status: 201, json: async () => ({}) });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Add item' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(within(dialog).getByText('Enter a name.')).toBeInTheDocument();
    expect(within(dialog).getByText('Enter a price of 0 or more.')).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Khaman' } });
    fireEvent.change(within(dialog).getByLabelText('Price (₹)'), { target: { value: '40' } });
    fireEvent.change(within(dialog).getByLabelText('GST rate (%)'), { target: { value: '18' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    const post = calls.find((c) => c.options?.method === 'POST');
    expect(post.url).toBe('/api/admin/menu/items');
    expect(JSON.parse(post.options.body)).toEqual({
      name: 'Khaman', price: 40, categoryId: 1, description: null, isVeg: true, isAvailable: true, taxRate: 18, sortOrder: 0,
    });
  });

  it('asks before deleting an item', async () => {
    const calls = mockApi({ ok: true, status: 204, json: async () => ({}) });
    renderPage();
    const row = (await screen.findByText('Samosa')).closest('tr');
    fireEvent.click(within(row).getByRole('button', { name: 'Delete' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('Delete Samosa?');
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

    expect(await screen.findByText('Samosa removed from the menu.')).toBeInTheDocument();
    expect(calls.find((c) => c.options?.method === 'DELETE').url).toBe('/api/admin/menu/items/10');
  });
});
