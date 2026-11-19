// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import DayReportPage from './DayReportPage';

const REPORT = {
  businessDate: '2026-11-15', restaurantName: 'Anand Thali', outletName: 'Main', gstin: '24ABCDE1234F1Z5', generatedAt: '2026-11-15T16:30:00Z',
  firstInvoice: 'INV/26-27/000001', lastInvoice: 'INV/26-27/000004', billCount: 3,
  itemsTotal: 1500, discount: 0, taxableValue: 1500, cgst: 49.2, sgst: 49.2, roundOff: 0.6, total: 1599,
  payments: [{ method: 'Cash', count: 1, amount: 533 }, { method: 'Upi', count: 1, amount: 533 }, { method: 'Card', count: 0, amount: 0 }],
  amountReceived: 1066, unpaidCount: 0, amountDue: 0,
  taxSummary: [{ taxRate: 5, taxableValue: 1500, cgst: 49.2, sgst: 49.2 }],
  items: [{ itemName: 'Paneer Tikka', quantity: 6, amount: 1500 }],
  voidCount: 0, voidTotal: 0, voidedBills: [], shifts: [],
};

const mock = (stock) => vi.stubGlobal('fetch', vi.fn(async (url) => {
  if (url === '/api/print-settings') return { ok: true, status: 200, json: async () => ({ paperWidth: 80, printLanguage: 'en', upiReady: false }) };
  if (url.startsWith('/api/reports/day')) return { ok: true, status: 200, json: async () => REPORT };
  if (url === '/api/stock' && stock) return { ok: true, status: 200, json: async () => stock };
  return { ok: false, status: 404, json: async () => ({}) };
}));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Day report low stock', () => {
  it('lists low-stock items and adds them to the WhatsApp summary', async () => {
    mock([
      { id: 1, name: 'Milk', unit: 'l', quantity: 2.5, lowLevel: 5, costPerUnit: null, isLow: true },
      { id: 2, name: 'Sugar', unit: 'kg', quantity: 10, lowLevel: 2, costPerUnit: null, isLow: false },
      { id: 3, name: 'Cups', unit: 'pcs', quantity: 40, lowLevel: 50, costPerUnit: null, isLow: true },
    ]);
    const open = vi.fn();
    vi.stubGlobal('open', open);
    render(<I18nProvider><DayReportPage /></I18nProvider>);
    expect(await screen.findByRole('heading', { name: 'Low stock' })).toBeInTheDocument();
    expect(screen.getByText('2.5 l (warn at 5)')).toBeInTheDocument();
    expect(screen.getByText('40 pcs (warn at 50)')).toBeInTheDocument();
    expect(screen.queryByText(/Sugar/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Share on WhatsApp' }));
    const text = decodeURIComponent(open.mock.calls[0][0].slice('https://wa.me/?text='.length));
    expect(text).toContain('Low stock: Milk 2.5 l, Cups 40 pcs');
  });

  it('shows nothing about stock when none is low or stock cannot be loaded', async () => {
    mock(null);
    render(<I18nProvider><DayReportPage /></I18nProvider>);
    expect(await screen.findByRole('button', { name: 'Share on WhatsApp' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Low stock' })).not.toBeInTheDocument();
  });
});
