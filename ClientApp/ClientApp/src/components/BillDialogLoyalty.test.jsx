// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { AuthProvider } from '../AuthContext';
import { ToastProvider } from '../ui/ToastContext';
import BillDialog from './BillDialog';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const BILL = {
  id: 7, orderId: 3, invoiceNumber: 'INV/26-27/000007', status: 'Unpaid', total: 500, discount: 0, amountPaid: 0, amountDue: 500,
  version: 1, payments: [], customerName: 'Asha', customerPhone: '98250 12345',
};
const SETTINGS = { enabled: true, rupeesPerPoint: 100, pointValue: 1 };
const GUEST_URL = `/api/customers/by-phone?phone=${encodeURIComponent('98250 12345')}`;

function mockApi(routes) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    const key = `${options?.method ?? 'GET'} ${url}`;
    calls.push({ key, body: options?.body ? JSON.parse(options.body) : undefined });
    if (url === '/api/auth/me') return { ok: true, status: 200, json: async () => ({ email: 'x@test.local', roles: ['cashier'] }) };
    if (url === '/api/print-settings') return { ok: true, status: 200, json: async () => ({ paperWidth: 80, printLanguage: 'en' }) };
    const route = routes[key];
    if (!route) return { ok: false, status: 404, json: async () => ({}) };
    const r = typeof route === 'function' ? route() : route;
    return { ok: r.status >= 200 && r.status < 300, status: r.status, json: async () => r.body };
  }));
  return calls;
}

const renderDialog = () => render(
  <I18nProvider initialLang="en">
    <AuthProvider>
      <ToastProvider>
        <BillDialog orderId={3} onClose={() => {}} />
      </ToastProvider>
    </AuthProvider>
  </I18nProvider>
);

describe('Bill loyalty', () => {
  it('shows the guest points and pays part of the bill with them', async () => {
    const calls = mockApi({
      'GET /api/orders/3/bill': { status: 200, body: BILL },
      'GET /api/customers/settings': { status: 200, body: SETTINGS },
      [`GET ${GUEST_URL}`]: { status: 200, body: { id: 1, name: 'Asha', phone: '9825012345', points: 120 } },
      'POST /api/bills/7/payments': { status: 200, body: { ...BILL, amountPaid: 120, amountDue: 380, version: 2, payments: [{ id: 1, method: 'Points', amount: 120 }] } },
    });
    renderDialog();
    expect(await screen.findByText('Asha: 120 points')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Use 120 points (₹120.00)' }));
    await waitFor(() => expect(calls.some((c) => c.key === 'POST /api/bills/7/payments')).toBe(true));
    expect(calls.find((c) => c.key === 'POST /api/bills/7/payments').body).toEqual({ method: 'Points', amount: 120, tendered: null, reference: null, version: 1 });
    expect(await screen.findByText(/Loyalty points ₹120\.00/)).toBeInTheDocument();
  });

  it('adds a new mobile number to loyalty only with consent', async () => {
    const calls = mockApi({
      'GET /api/orders/3/bill': { status: 200, body: BILL },
      'GET /api/customers/settings': { status: 200, body: SETTINGS },
      'POST /api/customers': { status: 201, body: { id: 2, name: 'Asha', phone: '9825012345', points: 0 } },
    });
    renderDialog();
    fireEvent.click(await screen.findByRole('button', { name: 'Add to loyalty' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add to loyalty' }));
    expect(await screen.findByText('Ask the guest first and tick the box.')).toBeInTheDocument();
    expect(calls.some((c) => c.key === 'POST /api/customers')).toBe(false);

    fireEvent.click(screen.getByLabelText(/The guest agreed/));
    fireEvent.click(screen.getByRole('button', { name: 'Add to loyalty' }));
    expect(await screen.findByText('Asha: 0 points')).toBeInTheDocument();
    expect(calls.find((c) => c.key === 'POST /api/customers').body).toEqual({ name: 'Asha', phone: '98250 12345', consent: true });
  });

  it('shows nothing when loyalty is off', async () => {
    mockApi({
      'GET /api/orders/3/bill': { status: 200, body: BILL },
      'GET /api/customers/settings': { status: 200, body: { ...SETTINGS, enabled: false } },
    });
    renderDialog();
    expect(await screen.findByRole('heading', { name: 'Bill INV/26-27/000007' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add to loyalty' })).not.toBeInTheDocument();
  });
});
