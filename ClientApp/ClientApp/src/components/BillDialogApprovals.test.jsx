// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { AuthProvider } from '../AuthContext';
import { ToastProvider } from '../ui/ToastContext';
import BillDialog from './BillDialog';

const BILL = {
  id: 7, orderId: 3, invoiceNumber: 'INV/26-27/000007', status: 'Paid', total: 500, discount: 0, amountPaid: 500, amountDue: 0,
  version: 2, printCount: 1, payments: [],
};

function mockApi(routes, roles) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    const key = `${options?.method ?? 'GET'} ${url}`;
    calls.push({ key, body: options?.body ? JSON.parse(options.body) : undefined });
    if (url === '/api/auth/me') return { ok: true, status: 200, json: async () => ({ email: 'x@test.local', roles }) };
    if (url === '/api/print-settings') return { ok: true, status: 200, json: async () => ({ paperWidth: 80, printLanguage: 'en', upiReady: false }) };
    const r = routes[key];
    if (!r) return { ok: false, status: 404, json: async () => ({}) };
    return { ok: r.status >= 200 && r.status < 300, status: r.status, json: async () => r.body };
  }));
  return calls;
}

const renderDialog = () => render(
  <I18nProvider><AuthProvider><ToastProvider><BillDialog orderId={3} onClose={() => {}} /></ToastProvider></AuthProvider></I18nProvider>,
);
const posted = (calls, key) => calls.filter((c) => c.key === key).map((c) => c.body);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Bill dialog approvals', () => {
  it('lets a cashier void with a manager PIN', async () => {
    const calls = mockApi({
      'GET /api/orders/3/bill': { status: 200, body: BILL },
      'POST /api/bills/7/void': { status: 200, body: { ...BILL, status: 'Void', version: 3 } },
    }, ['cashier']);
    renderDialog();
    fireEvent.click(await screen.findByRole('button', { name: 'Void bill' }));
    fireEvent.change(screen.getByLabelText('Reason for void'), { target: { value: 'Wrong table' } });
    fireEvent.click(screen.getByRole('button', { name: 'Yes, void this bill' }));
    expect(screen.getByText('Enter the manager PIN.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Manager PIN'), { target: { value: '4321' } });
    fireEvent.click(screen.getByRole('button', { name: 'Yes, void this bill' }));
    expect(await screen.findByRole('button', { name: 'Make bill' })).toBeInTheDocument();
    expect(posted(calls, 'POST /api/bills/7/void')).toEqual([{ reason: 'Wrong table', version: 2, approverPin: '4321' }]);
  });

  it('asks a cashier for a manager PIN before printing again', async () => {
    const calls = mockApi({
      'GET /api/orders/3/bill': { status: 200, body: BILL },
      'POST /api/approvals': { status: 200, body: { approvedBy: 'Meena' } },
    }, ['cashier']);
    const open = vi.fn();
    vi.stubGlobal('open', open);
    renderDialog();
    fireEvent.click(await screen.findByRole('button', { name: 'Print bill' }));
    expect(open).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('Manager PIN to print again'), { target: { value: '4321' } });
    fireEvent.click(screen.getByRole('button', { name: 'Approve and print' }));
    await waitFor(() => expect(open).toHaveBeenCalledWith('/print/bill/7?auto=1', '_blank', 'noopener'));
    expect(posted(calls, 'POST /api/approvals')).toEqual([{ pin: '4321', action: 'reprint', billId: 7 }]);
  });

  it('lets a manager print again without a PIN and opens the credit note form', async () => {
    mockApi({ 'GET /api/orders/3/bill': { status: 200, body: BILL } }, ['manager']);
    const open = vi.fn();
    vi.stubGlobal('open', open);
    renderDialog();
    fireEvent.click(await screen.findByRole('button', { name: 'Print bill' }));
    expect(open).toHaveBeenCalledWith('/print/bill/7?auto=1', '_blank', 'noopener');
    fireEvent.click(screen.getByRole('button', { name: 'Credit note' }));
    expect(screen.getByLabelText('Reason for credit note')).toBeInTheDocument();
    expect(screen.queryByLabelText('Manager PIN')).not.toBeInTheDocument();
  });

  it('hides void and credit note once a bill has a credit note', async () => {
    mockApi({ 'GET /api/orders/3/bill': { status: 200, body: { ...BILL, creditedAt: '2026-11-16T06:00:00Z' } } }, ['manager']);
    renderDialog();
    expect(await screen.findByRole('button', { name: 'Print bill' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Void bill' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Credit note' })).not.toBeInTheDocument();
  });
});
