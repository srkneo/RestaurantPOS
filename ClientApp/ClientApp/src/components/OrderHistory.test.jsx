// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import OrderHistory from './OrderHistory';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Order history', () => {
  it('lists orders newest first with totals', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({
      ok: true,
      json: async () => [
        { id: 1, tokenNumber: 1, tableId: 2, status: 'Ready', createdAt: '2026-11-14T07:00:00Z', items: [{ quantity: 2, itemName: 'Classic Burger', unitPrice: 6 }] },
        { id: 2, tokenNumber: 2, tableId: 3, status: 'Received', createdAt: '2026-11-14T08:00:00Z', items: [{ quantity: 1, itemName: 'Fries', unitPrice: 3 }] },
      ],
    })));
    render(<I18nProvider><OrderHistory /></I18nProvider>);

    const rows = await screen.findAllByRole('row');
    expect(within(rows[1]).getByText('1 x Fries')).toBeInTheDocument();
    expect(within(rows[2]).getByText('2 x Classic Burger')).toBeInTheDocument();
    expect(within(rows[2]).getByText('₹12.00')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Received')).toBeInTheDocument();
  });

  it('shows an error with retry', async () => {
    const fetchMock = vi.fn(async () => ({ ok: false, json: async () => ({}) }));
    vi.stubGlobal('fetch', fetchMock);
    render(<I18nProvider><OrderHistory /></I18nProvider>);

    fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
