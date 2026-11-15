// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { AuthProvider } from '../AuthContext';
import { ToastProvider } from '../ui/ToastContext';
import DayClosePage from './DayClosePage';

const SHIFT = {
  id: 1, status: 'Open', businessDate: '2026-11-15', openedAt: '2026-11-15T04:00:00Z', openingCash: 1000, cashSales: 533,
  cashIn: 100, cashOut: 200, expectedCash: 1433, closedAt: null, countedCash: null, difference: null, closeNote: null, version: 3,
  movements: [{ id: 1, kind: 'Out', amount: 200, reason: 'Milk vendor', at: '2026-11-15T06:00:00Z' }],
};

const ok = (body) => ({ ok: true, status: 200, json: async () => body });

function mockApi(routes, roles = ['cashier']) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    const key = `${options?.method ?? 'GET'} ${url}`;
    calls.push({ key, body: options?.body ? JSON.parse(options.body) : undefined });
    if (url === '/api/auth/me') return ok({ email: 'x@test.local', roles });
    const route = routes[key];
    if (!route) return { ok: false, status: 404, json: async () => ({}) };
    return typeof route === 'function' ? route() : route;
  }));
  return calls;
}

const renderPage = () => render(
  <MemoryRouter><I18nProvider><AuthProvider><ToastProvider><DayClosePage /></ToastProvider></AuthProvider></I18nProvider></MemoryRouter>,
);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Day close page', () => {
  it('opens a shift with the cash in the drawer', async () => {
    const calls = mockApi({
      'GET /api/shifts/current': ok({ shift: null }),
      'POST /api/shifts/open': ok({ ...SHIFT, openingCash: 1000, cashSales: 0, cashIn: 0, cashOut: 0, expectedCash: 1000, version: 1, movements: [] }),
    });
    renderPage();
    fireEvent.change(await screen.findByLabelText('Opening cash'), { target: { value: '1000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Open shift' }));

    expect(await screen.findByText('Shift opened.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Current shift' })).toBeInTheDocument();
    expect(calls.find((c) => c.key === 'POST /api/shifts/open').body).toEqual({ openingCash: 1000 });
  });

  it('records cash out, shows short before closing, then closes the shift', async () => {
    const afterMove = { ...SHIFT, cashOut: 250, expectedCash: 1383, version: 4,
      movements: [...SHIFT.movements, { id: 2, kind: 'Out', amount: 50, reason: 'Tea', at: '2026-11-15T07:00:00Z' }] };
    const calls = mockApi({
      'GET /api/shifts/current': ok({ shift: SHIFT }),
      'POST /api/shifts/current/movements': ok(afterMove),
      'POST /api/shifts/current/close': ok({ ...afterMove, status: 'Closed', closedAt: '2026-11-15T16:00:00Z', countedCash: 1350, difference: -33, version: 5 }),
    });
    renderPage();
    expect(await screen.findByText('₹1433.00')).toBeInTheDocument();
    expect(screen.getByText(/Milk vendor/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '50' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: ' Tea ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('₹1383.00')).toBeInTheDocument();
    expect(calls.find((c) => c.key === 'POST /api/shifts/current/movements').body).toEqual({ kind: 'Out', amount: 50, reason: 'Tea' });

    fireEvent.change(screen.getByLabelText('Counted cash'), { target: { value: '1350' } });
    expect(screen.getByTestId('day-preview')).toHaveTextContent('Short by ₹33.00');
    fireEvent.click(screen.getByRole('button', { name: 'Close shift' }));

    expect(await screen.findByRole('heading', { name: 'Shift closed' })).toBeInTheDocument();
    expect(screen.getByText('Short by ₹33.00')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'No shift is open' })).toBeInTheDocument();
    expect(calls.find((c) => c.key === 'POST /api/shifts/current/close').body).toEqual({ countedCash: 1350, note: null, version: 4 });
  });

  it('shows the server message and reloads when another till changed the shift', async () => {
    const calls = mockApi({
      'GET /api/shifts/current': ok({ shift: null }),
      'POST /api/shifts/open': { ok: false, status: 409, json: async () => ({ message: 'A shift is already open. Close it first.' }) },
    });
    renderPage();
    fireEvent.change(await screen.findByLabelText('Opening cash'), { target: { value: '500' } });
    fireEvent.click(screen.getByRole('button', { name: 'Open shift' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('A shift is already open. Close it first.');
    await waitFor(() => expect(calls.filter((c) => c.key === 'GET /api/shifts/current')).toHaveLength(2));
  });

  it('checks the amount before sending', async () => {
    const calls = mockApi({ 'GET /api/shifts/current': ok({ shift: null }) });
    renderPage();
    fireEvent.change(await screen.findByLabelText('Opening cash'), { target: { value: 'abc' } });
    fireEvent.click(screen.getByRole('button', { name: 'Open shift' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Enter a valid amount.');
    expect(calls.some((c) => c.key === 'POST /api/shifts/open')).toBe(false);
  });

  it('links to the day report for managers only', async () => {
    mockApi({ 'GET /api/shifts/current': ok({ shift: null }) }, ['manager']);
    renderPage();
    expect(await screen.findByRole('link', { name: 'Day report' })).toHaveAttribute('href', '/reports/day');
    cleanup();
    vi.unstubAllGlobals();

    mockApi({ 'GET /api/shifts/current': ok({ shift: null }) }, ['cashier']);
    renderPage();
    await screen.findByLabelText('Opening cash');
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/auth/me'));
    expect(screen.queryByRole('link', { name: 'Day report' })).not.toBeInTheDocument();
  });
});
