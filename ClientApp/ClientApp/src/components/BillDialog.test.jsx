// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { AuthProvider } from '../AuthContext';
import { ToastProvider } from '../ui/ToastContext';
import BillDialog, { parseAmount } from './BillDialog';

const BILL = {
  id: 7, orderId: 3, invoiceNumber: 'INV/26-27/000007', status: 'Unpaid', total: 500, discount: 0, amountPaid: 0, amountDue: 500,
  version: 1, payments: [],
};

// routes: { 'GET /api/orders/3/bill': {status, body}, 'POST /api/...': ... }
function mockApi(routes, roles = ['cashier']) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    const key = `${options?.method ?? 'GET'} ${url}`;
    calls.push({ key, body: options?.body ? JSON.parse(options.body) : undefined });
    if (url === '/api/auth/me') return { ok: true, status: 200, json: async () => ({ email: 'x@test.local', roles }) };
    if (url === '/api/print-settings') return { ok: true, status: 200, json: async () => ({ paperWidth: 80, printLanguage: 'en', upiReady: true }) };
    const route = routes[key];
    if (!route) return { ok: false, status: 404, json: async () => ({}) };
    const r = typeof route === 'function' ? route() : route;
    return { ok: r.status >= 200 && r.status < 300, status: r.status, json: async () => r.body };
  }));
  return calls;
}

const renderDialog = (props = {}) => render(
  <I18nProvider initialLang="en">
    <AuthProvider>
      <ToastProvider>
        <BillDialog orderId={3} onClose={() => {}} {...props} />
      </ToastProvider>
    </AuthProvider>
  </I18nProvider>
);

const posted = (calls, key) => calls.filter((c) => c.key === key).map((c) => c.body);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Bill dialog', () => {
  it('makes the bill with a percent discount, then shows what is due', async () => {
    const calls = mockApi({ 'POST /api/orders/3/bill': { status: 201, body: { ...BILL, discount: 50, total: 450, amountDue: 450 } } });
    const onChanged = vi.fn();
    renderDialog({ onChanged });

    fireEvent.click(await screen.findByLabelText('Percent off'));
    fireEvent.change(screen.getByLabelText('Discount (%)'), { target: { value: '10' } });
    fireEvent.click(screen.getByRole('button', { name: 'Make bill' }));

    expect(await screen.findByRole('heading', { name: 'Bill INV/26-27/000007' })).toBeInTheDocument();
    expect(posted(calls, 'POST /api/orders/3/bill')).toEqual([{ discountPercent: 10 }]);
    expect(screen.getAllByText('₹450.00')).toHaveLength(2);
    expect(screen.getByLabelText('Amount')).toHaveValue('450');
    expect(onChanged).toHaveBeenCalled();
  });

  it('takes cash, shows the change, and marks the bill paid', async () => {
    const calls = mockApi({
      'GET /api/orders/3/bill': { status: 200, body: BILL },
      'POST /api/bills/7/payments': { status: 200, body: { ...BILL, status: 'Paid', amountPaid: 500, amountDue: 0, version: 2,
        payments: [{ id: 1, method: 'Cash', amount: 500, tendered: 600, change: 100 }] } },
    });
    renderDialog();

    fireEvent.change(await screen.findByLabelText('Cash received'), { target: { value: '600' } });
    expect(screen.getByText('Give back ₹100.00')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Record payment' }));

    expect(await screen.findByText('Paid in full')).toBeInTheDocument();
    expect(posted(calls, 'POST /api/bills/7/payments')).toEqual([{ method: 'Cash', amount: 500, tendered: 600, reference: null, version: 1 }]);
    expect(screen.getByText('Bill INV/26-27/000007 is paid.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Record payment' })).not.toBeInTheDocument();
  });

  it('shows the UPI QR and sends the reference; part payments leave the rest due', async () => {
    const calls = mockApi({
      'GET /api/orders/3/bill': { status: 200, body: BILL },
      'POST /api/bills/7/payments': { status: 200, body: { ...BILL, amountPaid: 300, amountDue: 200, version: 2,
        payments: [{ id: 1, method: 'Upi', amount: 300, tendered: null, change: null }] } },
    });
    renderDialog();

    fireEvent.click(await screen.findByRole('button', { name: 'UPI' }));
    expect(screen.getByAltText('Customer scans to pay')).toHaveAttribute('src', '/api/bills/7/upi-qr.svg?due=500');
    expect(screen.queryByLabelText('Cash received')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '300' } });
    fireEvent.change(screen.getByLabelText('Reference (optional)'), { target: { value: 'UTR 4521' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record payment' }));

    await waitFor(() => expect(screen.getByLabelText('Amount')).toHaveValue('200'));
    expect(posted(calls, 'POST /api/bills/7/payments')).toEqual([{ method: 'Upi', amount: 300, tendered: null, reference: 'UTR 4521', version: 1 }]);
    expect(screen.getByText('UPI ₹300.00')).toBeInTheDocument();
  });

  it('checks amounts before sending', async () => {
    const calls = mockApi({ 'GET /api/orders/3/bill': { status: 200, body: BILL } });
    renderDialog();

    fireEvent.change(await screen.findByLabelText('Amount'), { target: { value: '600' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record payment' }));
    expect(screen.getByText('Only ₹500.00 is due.')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '500' } });
    fireEvent.change(screen.getByLabelText('Cash received'), { target: { value: '400' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record payment' }));
    expect(screen.getByText('Cash received is less than the amount.')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: 'abc' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record payment' }));
    expect(screen.getByText('Enter an amount.')).toBeInTheDocument();
    expect(posted(calls, 'POST /api/bills/7/payments')).toEqual([]);
  });

  it('records a tip for the waiter, apart from the bill', async () => {
    const calls = mockApi({
      'GET /api/orders/3/bill': { status: 200, body: { ...BILL, status: 'Paid', amountPaid: 500, amountDue: 0 } },
      'POST /api/bills/7/tips': { status: 201, body: { id: 1, amount: 50, waiterName: 'Ravi' } },
    });
    renderDialog();
    fireEvent.click(await screen.findByRole('button', { name: 'Add tip' }));
    fireEvent.change(screen.getByLabelText('Tip amount'), { target: { value: '50' } });
    fireEvent.change(screen.getByLabelText('Tip paid by'), { target: { value: 'Upi' } });
    fireEvent.change(screen.getByLabelText('Waiter'), { target: { value: 'Ravi' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save tip' }));

    expect(await screen.findByText('Tip of ₹50.00 saved for Ravi.')).toBeInTheDocument();
    expect(posted(calls, 'POST /api/bills/7/tips')).toEqual([{ method: 'Upi', amount: 50, waiterName: 'Ravi' }]);
  });

  it('opens the bill print page', async () => {
    mockApi({ 'GET /api/orders/3/bill': { status: 200, body: BILL } });
    const open = vi.fn();
    vi.stubGlobal('open', open);
    renderDialog();
    fireEvent.click(await screen.findByRole('button', { name: 'Print bill' }));
    expect(open).toHaveBeenCalledWith('/print/bill/7?auto=1', '_blank', 'noopener');
  });

  it('lets a manager void the bill with a reason; cashiers cannot', async () => {
    mockApi({ 'GET /api/orders/3/bill': { status: 200, body: BILL } }, ['cashier']);
    renderDialog();
    expect(await screen.findByRole('button', { name: 'Print bill' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Void bill' }));
    expect(screen.getByLabelText('Manager PIN')).toBeInTheDocument();  // cashiers void with a manager PIN
    cleanup();
    vi.unstubAllGlobals();

    const calls = mockApi({
      'GET /api/orders/3/bill': { status: 200, body: BILL },
      'POST /api/bills/7/void': { status: 200, body: { ...BILL, status: 'Void', version: 2 } },
    }, ['manager']);
    renderDialog();
    fireEvent.click(await screen.findByRole('button', { name: 'Void bill' }));
    fireEvent.click(screen.getByRole('button', { name: 'Yes, void this bill' }));
    expect(screen.getByText('Give a reason.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Reason for void'), { target: { value: 'Wrong items' } });
    fireEvent.click(screen.getByRole('button', { name: 'Yes, void this bill' }));

    expect(await screen.findByRole('button', { name: 'Make bill' })).toBeInTheDocument();
    expect(posted(calls, 'POST /api/bills/7/void')).toEqual([{ reason: 'Wrong items', version: 1 }]);
  });

  it('shows nothing and loads nothing without an order', () => {
    const calls = mockApi({});
    renderDialog({ orderId: null });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(calls.filter((c) => c.key.includes('/bill'))).toEqual([]);
  });

  it('reads amounts written the Indian way', () => {
    expect(parseAmount('₹1,250.50')).toBe(1250.5);
    expect(parseAmount('')).toBeNaN();
    expect(parseAmount('12a')).toBeNaN();
  });
});
