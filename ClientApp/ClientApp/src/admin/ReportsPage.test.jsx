// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import ReportsPage from './ReportsPage';

const REPORT = {
  from: '2026-11-01', to: '2026-11-18', billCount: 2, itemsTotal: 100, discount: 0, taxableValue: 100, cgst: 0, sgst: 0, total: 100,
  creditTotal: 0, netSales: 100,
  days: [{ date: '2026-11-18', bills: 2, total: 100 }],
  items: [{ itemName: 'Butter Naan', quantity: 2, amount: 80 }, { itemName: 'Tea', quantity: 1, amount: 20 }],
  categories: [{ category: 'Breads', quantity: 2, amount: 80 }, { category: 'Drinks', quantity: 1, amount: 20 }],
  payments: [{ method: 'Cash', count: 1, amount: 80 }, { method: 'Upi', count: 0, amount: 0 }, { method: 'Card', count: 0, amount: 0 }],
  hours: [{ hour: 13, bills: 2, total: 100 }],
  staff: [{ name: null, bills: 2, total: 100 }],
  taxSummary: [],
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Reports page', () => {
  it('shows sales by category, payment and staff, and offers the GST CSV for the chosen days', async () => {
    const urls = [];
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      urls.push(url);
      return { ok: true, status: 200, json: async () => REPORT };
    }));
    render(<I18nProvider><ReportsPage /></I18nProvider>);

    const categories = await screen.findByRole('region', { name: 'Sales by category' });
    expect(within(categories).getByText('Breads')).toBeInTheDocument();
    expect(within(categories).getByText('₹80.00')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Sales by staff' })).getByText('Not recorded')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Payments' })).getByText('Cash')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-11-01' } });
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-11-18' } });
    fireEvent.click(screen.getByRole('button', { name: 'Show' }));
    await vi.waitFor(() => expect(urls).toContain('/api/reports/sales?from=2026-11-01&to=2026-11-18'));
    expect(screen.getByRole('link', { name: 'Download GST bills (CSV)' })).toHaveAttribute('href', '/api/reports/gst.csv?from=2026-11-01&to=2026-11-18');
    expect(screen.getByRole('link', { name: 'Download for Tally (XML)' })).toHaveAttribute('href', '/api/reports/tally.xml?from=2026-11-01&to=2026-11-18');
    expect(screen.getByRole('link', { name: 'Download sales (CSV)' })).toHaveAttribute('href', '/api/reports/sales.csv?from=2026-11-01&to=2026-11-18');
  });

  it('shows the server message for a range that is too long', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: false, status: 400, json: async () => ({ message: 'Choose at most 92 days.' }) })));
    render(<I18nProvider><ReportsPage /></I18nProvider>);
    expect(await screen.findByRole('alert')).toHaveTextContent('Choose at most 92 days.');
  });
});
