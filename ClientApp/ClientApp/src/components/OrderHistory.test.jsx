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

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Order history', () => {
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
