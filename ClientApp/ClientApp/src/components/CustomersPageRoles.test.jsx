// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { AuthProvider } from '../AuthContext';
import { ToastProvider } from '../ui/ToastContext';
import CustomersPage from './CustomersPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const asha = { id: 1, name: 'Asha', phone: '9825012345', points: 12, visits: 3, spent: 1250, lastVisitAt: '2026-11-19T10:00:00Z', consentAt: '2026-11-01T10:00:00Z' };

// routes: { 'GET /api/customers': () => [status, body], ... }; unknown routes give 404.
function mockApi(routes, roles) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    const key = `${options?.method ?? 'GET'} ${url}`;
    calls.push({ key, body: options?.body ? JSON.parse(options.body) : undefined });
    if (url === '/api/auth/me') return { ok: true, status: 200, json: async () => ({ email: 'x@test.local', roles }) };
    const route = routes[key];
    if (!route) return { ok: false, status: 404, json: async () => ({}) };
    const [status, body] = route();
    return { ok: status < 300, status, json: async () => body };
  }));
  return calls;
}

const show = () => render(
  <I18nProvider initialLang="en">
    <AuthProvider>
      <ToastProvider>
        <CustomersPage />
      </ToastProvider>
    </AuthProvider>
  </I18nProvider>
);

const SETTINGS = { enabled: false, rupeesPerPoint: 100, pointValue: 1 };

describe('Loyalty guests page by role', () => {
  it('manager forgets a guest after confirming', async () => {
    let list = [asha];
    const calls = mockApi({
      'GET /api/customers': () => [200, list],
      'GET /api/customers/settings': () => [200, SETTINGS],
      'DELETE /api/customers/1': () => { list = []; return [204, null]; },
    }, ['manager']);
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Forget Asha' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/deleted for good/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Forget' }));
    await waitFor(() => expect(calls.some((c) => c.key === 'DELETE /api/customers/1')).toBe(true));
    expect(await screen.findByText('No loyalty guests yet. Add a guest who agrees to collect points.')).toBeInTheDocument();
  });

  it('cashier sees no Forget button and no loyalty settings', async () => {
    mockApi({ 'GET /api/customers': () => [200, [asha]], 'GET /api/customers/settings': () => [200, SETTINGS] }, ['cashier']);
    show();
    expect(await screen.findByText('Asha')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Forget Asha' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Loyalty points' })).not.toBeInTheDocument();
  });

  it('edits a guest without asking consent again', async () => {
    const calls = mockApi({
      'GET /api/customers': () => [200, [asha]],
      'PUT /api/customers/1': () => [200, { ...asha, name: 'Asha Patel' }],
    }, ['cashier']);
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Edit Asha' }));
    expect(screen.getByLabelText('Name')).toHaveValue('Asha');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Asha Patel' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(calls.some((c) => c.key === 'PUT /api/customers/1')).toBe(true));
    expect(calls.find((c) => c.key === 'PUT /api/customers/1').body).toEqual({ name: 'Asha Patel', phone: '9825012345', consent: true });
  });

  it('shows the server error under the mobile field', async () => {
    mockApi({
      'GET /api/customers': () => [200, []],
      'POST /api/customers': () => [400, { errors: { phone: ['A guest with this mobile number is already in the list.'] } }],
    }, ['cashier']);
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Add guest' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ravi' } });
    fireEvent.change(screen.getByLabelText('Mobile number'), { target: { value: '9825012345' } });
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('A guest with this mobile number is already in the list.')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('manager saves loyalty settings and sees a range error', async () => {
    let fail = true;
    const calls = mockApi({
      'GET /api/customers': () => [200, []],
      'GET /api/customers/settings': () => [200, SETTINGS],
      'PUT /api/customers/settings': () => (fail
        ? [400, { errors: { rupeesPerPoint: ['Enter 1 to 10000 rupees for one point.'] } }]
        : [200, { enabled: true, rupeesPerPoint: 50, pointValue: 1 }]),
    }, ['manager']);
    show();
    fireEvent.click(await screen.findByLabelText('Give loyalty points on paid bills'));
    fireEvent.change(screen.getByLabelText('Rupees for 1 point'), { target: { value: '0' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save loyalty settings' }));
    expect(await screen.findByText('Enter 1 to 10000 rupees for one point.')).toBeInTheDocument();

    fail = false;
    fireEvent.change(screen.getByLabelText('Rupees for 1 point'), { target: { value: '50' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save loyalty settings' }));
    await waitFor(() => expect(screen.queryByText('Enter 1 to 10000 rupees for one point.')).not.toBeInTheDocument());
    const puts = calls.filter((c) => c.key === 'PUT /api/customers/settings');
    expect(puts[puts.length - 1].body).toEqual({ enabled: true, rupeesPerPoint: 50, pointValue: 1 });
  });

  it('searches by what is typed and retries after an error', async () => {
    let broken = true;
    const calls = mockApi({
      'GET /api/customers': () => (broken ? [500, {}] : [200, [asha]]),
      'GET /api/customers?search=asha': () => [200, [asha]],
    }, ['cashier']);
    show();
    const retry = await screen.findByRole('button', { name: 'Try again' });
    broken = false;
    fireEvent.click(retry);
    expect(await screen.findByText('Asha')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Search by name or mobile'), { target: { value: 'asha' } });
    await waitFor(() => expect(calls.some((c) => c.key === 'GET /api/customers?search=asha')).toBe(true));
  });

  it('shows the points history of a guest', async () => {
    mockApi({
      'GET /api/customers': () => [200, [asha]],
      'GET /api/customers/1/moves': () => [200, [
        { id: 2, kind: 'Redeem', points: -10, balanceAfter: 12, at: '2026-11-19T10:00:00Z', note: 'INV/26-27/000009' },
        { id: 1, kind: 'Earn', points: 22, balanceAfter: 22, at: '2026-11-18T10:00:00Z', note: 'INV/26-27/000004' },
      ]],
    }, ['cashier']);
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Points history Asha' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Spent')).toBeInTheDocument();
    expect(within(dialog).getByText('+22')).toBeInTheDocument();
    expect(within(dialog).getByText('-10')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });
});
