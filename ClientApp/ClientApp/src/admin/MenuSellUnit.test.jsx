// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import MenuPage from './MenuPage';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

const CATEGORIES = [{ id: 1, name: 'Dairy', sortOrder: 1, isActive: true }];
const ITEMS = [
  { id: 20, name: 'Paneer', price: 400, categoryId: 1, category: 'Dairy', description: null, isVeg: true, isAvailable: true, taxRate: 5, sortOrder: 1, sellUnit: 'kg' },
];

function mockApi() {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (options?.method) return { ok: true, status: 200, json: async () => ({}) };
    if (url.endsWith('/categories')) return { ok: true, json: async () => CATEGORIES };
    if (url.endsWith('/stations')) return { ok: true, json: async () => [] };
    return { ok: true, json: async () => ITEMS };
  }));
  render(<MemoryRouter><I18nProvider><ToastProvider><MenuPage /></ToastProvider></I18nProvider></MemoryRouter>);
  return calls;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Menu item sold by', () => {
  it('saves a new item sold by the litre', async () => {
    const calls = mockApi();
    fireEvent.click(await screen.findByRole('button', { name: 'Add item' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Chaas' } });
    fireEvent.change(within(dialog).getByLabelText('Price (₹)'), { target: { value: '60' } });
    expect(within(dialog).getByLabelText('Sold by')).toHaveValue('piece');
    fireEvent.change(within(dialog).getByLabelText('Sold by'), { target: { value: 'litre' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(calls.some((c) => c.options?.method === 'POST')).toBe(true));
    expect(JSON.parse(calls.find((c) => c.options?.method === 'POST').options.body)).toMatchObject({ name: 'Chaas', price: 60, sellUnit: 'litre' });
  });

  it('keeps kg when an item is edited', async () => {
    const calls = mockApi();
    const row = (await screen.findByText('Paneer')).closest('tr');
    fireEvent.click(within(row).getByRole('button', { name: 'Edit' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByLabelText('Sold by')).toHaveValue('kg');
    expect(within(dialog).getByRole('option', { name: 'Per kg' })).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(calls.some((c) => c.options?.method === 'PUT')).toBe(true));
    expect(JSON.parse(calls.find((c) => c.options?.method === 'PUT').options.body)).toMatchObject({ sellUnit: 'kg' });
  });
});
