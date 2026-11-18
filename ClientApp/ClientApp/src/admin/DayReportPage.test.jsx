// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import DayReportPage from './DayReportPage';

const REPORT = {
  businessDate: '2026-11-15', restaurantName: 'Anand Thali', outletName: 'Main', gstin: '24ABCDE1234F1Z5', generatedAt: '2026-11-15T16:30:00Z',
  firstInvoice: 'INV/26-27/000001', lastInvoice: 'INV/26-27/000004', billCount: 3,
  itemsTotal: 1500, discount: 0, taxableValue: 1500, cgst: 49.2, sgst: 49.2, roundOff: 0.6, total: 1599,
  payments: [{ method: 'Cash', count: 1, amount: 533 }, { method: 'Upi', count: 1, amount: 533 }, { method: 'Card', count: 0, amount: 0 }],
  amountReceived: 1066, unpaidCount: 1, amountDue: 533,
  taxSummary: [{ taxRate: 5, taxableValue: 1320, cgst: 33, sgst: 33 }, { taxRate: 18, taxableValue: 180, cgst: 16.2, sgst: 16.2 }],
  items: [{ itemName: 'Paneer Tikka', quantity: 6, amount: 1320 }, { itemName: 'Cola', quantity: 3, amount: 180 }],
  voidCount: 1, voidTotal: 533, voidedBills: [{ id: 4, invoiceNumber: 'INV/26-27/000004', total: 533, reason: 'Customer left' }],
  shifts: [{ id: 1, status: 'Closed', openedAt: '2026-11-15T04:00:00Z', closedAt: '2026-11-15T16:00:00Z', openingCash: 1000, cashSales: 533,
    cashIn: 0, cashOut: 200, expectedCash: 1333, countedCash: 1300, difference: -33, movements: [] }],
};

function mockApi() {
  const urls = [];
  vi.stubGlobal('fetch', vi.fn(async (url) => {
    urls.push(url);
    if (url === '/api/print-settings') return { ok: true, status: 200, json: async () => ({ paperWidth: 80, printLanguage: 'en', upiReady: false }) };
    if (url.startsWith('/api/reports/day')) return { ok: true, status: 200, json: async () => REPORT };
    return { ok: false, status: 404, json: async () => ({}) };
  }));
  return urls;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Day report page', () => {
  it("shares the day's summary on WhatsApp", async () => {
    mockApi();
    const open = vi.fn();
    vi.stubGlobal('open', open);
    render(<I18nProvider><DayReportPage /></I18nProvider>);
    fireEvent.click(await screen.findByRole('button', { name: 'Share on WhatsApp' }));
    const url = open.mock.calls[0][0];
    expect(url.startsWith('https://wa.me/?text=')).toBe(true);
    const text = decodeURIComponent(url.slice('https://wa.me/?text='.length));
    expect(text).toContain('Anand Thali – Main · 2026-11-15');
    expect(text).toContain('Bills: 3 · Sales ₹1599.00');
    expect(text).toContain('Unpaid: 1');
  });

  it('mentions the guest rating in the WhatsApp summary', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      if (url === '/api/print-settings') return { ok: true, status: 200, json: async () => ({ paperWidth: 80, printLanguage: 'en', upiReady: false }) };
      if (url.startsWith('/api/reports/day')) return { ok: true, status: 200, json: async () => ({ ...REPORT, feedbackCount: 4, feedbackAverage: 4.3 }) };
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    const open = vi.fn();
    vi.stubGlobal('open', open);
    render(<I18nProvider><DayReportPage /></I18nProvider>);
    fireEvent.click(await screen.findByRole('button', { name: 'Share on WhatsApp' }));
    expect(decodeURIComponent(open.mock.calls[0][0])).toContain('Guest rating 4.3 of 5 (4 guests)');
  });

  it('shows tips and expenses on the Z report', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      if (url === '/api/print-settings') return { ok: true, status: 200, json: async () => ({ paperWidth: 80, printLanguage: 'en', upiReady: false }) };
      if (url.startsWith('/api/reports/day')) return { ok: true, status: 200, json: async () => ({ ...REPORT, tipTotal: 80, expenseTotal: 5120, expenseFromTill: 120 }) };
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    render(<I18nProvider><DayReportPage /></I18nProvider>);
    expect(await screen.findByText('Tips (not in sales)')).toBeInTheDocument();
    expect(screen.getByText('5120.00')).toBeInTheDocument();
    expect(screen.getByText('Expenses from till')).toBeInTheDocument();
  });

  it('shows tips per waiter under the report', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      if (url === '/api/print-settings') return { ok: true, status: 200, json: async () => ({ paperWidth: 80, printLanguage: 'en', upiReady: false }) };
      if (url.startsWith('/api/reports/day')) return { ok: true, status: 200, json: async () => REPORT };
      if (url.startsWith('/api/tips')) {
        return { ok: true, status: 200, json: async () => ({ businessDate: '2026-11-15', total: 80, tips: [],
          waiters: [{ waiterName: 'Ravi', count: 2, amount: 70, cash: 50 }, { waiterName: 'Shared', count: 1, amount: 10, cash: 0 }] }) };
      }
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    render(<I18nProvider><DayReportPage /></I18nProvider>);
    expect(await screen.findByText('Tips per waiter')).toBeInTheDocument();
    expect(screen.getByText('Ravi')).toBeInTheDocument();
    expect(screen.getByText('Shared pool')).toBeInTheDocument();
    expect(screen.getByText('₹70.00')).toBeInTheDocument();
  });

  it('shows the Z report of today and prints it', async () => {
    const urls = mockApi();
    const open = vi.fn();
    vi.stubGlobal('open', open);
    render(<I18nProvider><DayReportPage /></I18nProvider>);

    expect(await screen.findByText('Z REPORT')).toBeInTheDocument();
    expect(urls).toContain('/api/reports/day');
    expect(screen.getByLabelText('Business day')).toHaveValue('2026-11-15');
    expect(screen.getByText('1599.00')).toBeInTheDocument();
    expect(screen.getByText('Cash (1)')).toBeInTheDocument();
    expect(screen.getByText('Unpaid (1)')).toBeInTheDocument();
    expect(screen.getByText('5% (2.5+2.5)')).toBeInTheDocument();
    expect(screen.getByText('Paneer Tikka x 6')).toBeInTheDocument();
    expect(screen.getByText(/Customer left/)).toBeInTheDocument();
    expect(screen.getByText('SHORT')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Print Z report' }));
    expect(open).toHaveBeenCalledWith('/print/day?date=2026-11-15&auto=1', '_blank', 'noopener');
  });

  it('loads another day', async () => {
    const urls = mockApi();
    render(<I18nProvider><DayReportPage /></I18nProvider>);
    fireEvent.change(await screen.findByLabelText('Business day'), { target: { value: '2026-11-14' } });
    fireEvent.click(screen.getByRole('button', { name: 'Show' }));
    await waitFor(() => expect(urls).toContain('/api/reports/day?date=2026-11-14'));
  });
});
