// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { PrintBillPage, PrintKotPage } from './PrintPages';

const BILL = {
  id: 7, invoiceNumber: 'INV/26-27/000007', taxMode: 'None', taxNote: null, issuedAt: '2026-11-15T06:30:00Z', status: 'Unpaid',
  orderLabel: 'Takeaway', tokenNumber: 12, itemsTotal: 100, discount: 0, taxableValue: 100, cgst: 0, sgst: 0, roundOff: 0, total: 100,
  amountDue: 100, printCount: 0, store: { restaurantName: 'Anand Thali House' }, lines: [{ itemName: 'Tea', quantity: 5, unitPrice: 20, amount: 100 }],
  taxSummary: [], payments: [],
};

function mockApi({ bill = BILL, settings = { paperWidth: 80, printLanguage: 'en', upiReady: true }, billStatus = 200 } = {}) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/print-settings') return { ok: true, json: async () => settings };
    if (url === '/api/bills/7') return { ok: billStatus === 200, status: billStatus, json: async () => bill };
    if (url === '/api/bills/7/printed') return { ok: true, json: async () => ({ printCount: 1 }) };
    if (url === '/api/orders/3') return { ok: true, json: async () => ({ id: 3, tokenNumber: 12, orderType: 'Takeaway', customerName: 'Ravi', createdAt: '2026-11-15T06:30:00Z', items: [] }) };
    return { ok: false, status: 404, json: async () => ({}) };
  }));
  return calls;
}

const renderAt = (path) => render(
  <MemoryRouter initialEntries={[path]}>
    <I18nProvider initialLang="en">
      <Routes>
        <Route path="/print/bill/:id" element={<PrintBillPage />} />
        <Route path="/print/kot/:id" element={<PrintKotPage />} />
      </Routes>
    </I18nProvider>
  </MemoryRouter>
);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Print pages', () => {
  it('prints the bill automatically once, counts it, and later prints say DUPLICATE', async () => {
    const calls = mockApi();
    const print = vi.fn();
    vi.stubGlobal('print', print);
    renderAt('/print/bill/7?auto=1');

    expect(await screen.findByText('INV/26-27/000007')).toBeInTheDocument();
    expect(screen.getByRole('img')).toHaveAttribute('src', '/api/bills/7/upi-qr.svg?due=100');
    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(calls.some((c) => c.url === '/api/bills/7/printed' && c.options?.method === 'POST')).toBe(true));
    expect(await screen.findByText('DUPLICATE')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Print' }));
    await waitFor(() => expect(print).toHaveBeenCalledTimes(2));
  });

  it('does not print by itself without auto, and marks an already printed bill', async () => {
    mockApi({ bill: { ...BILL, printCount: 2 } });
    const print = vi.fn();
    vi.stubGlobal('print', print);
    renderAt('/print/bill/7');

    expect(await screen.findByText('DUPLICATE')).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 500));
    expect(print).not.toHaveBeenCalled();
  });

  it('has no QR when UPI is not set up', async () => {
    mockApi({ settings: { paperWidth: 58, printLanguage: 'en', upiReady: false } });
    vi.stubGlobal('print', vi.fn());
    renderAt('/print/bill/7');
    expect(await screen.findByText('INV/26-27/000007')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.getByTestId('bill-receipt')).toHaveClass('receipt-58');
  });

  it('shows an error with retry when the bill cannot be loaded', async () => {
    mockApi({ billStatus: 404 });
    vi.stubGlobal('print', vi.fn());
    renderAt('/print/bill/7');
    expect(await screen.findByRole('button', { name: /try again|retry/i })).toBeInTheDocument();
  });

  it('prints a KOT in the outlet language', async () => {
    mockApi({ settings: { paperWidth: 80, printLanguage: 'gu', upiReady: false } });
    const print = vi.fn();
    vi.stubGlobal('print', print);
    renderAt('/print/kot/3?auto=1');
    expect(await screen.findByText('ટેકઅવે')).toBeInTheDocument();
    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));
  });
});
