// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import ShopLicencesPage from './ShopLicencesPage';

// SUP2: the platform shows today's support code for one shop PC.
const licence = (changes = {}) => ({
  id: 3, key: 'POS-AAAAA-BBBBB-CCCCC-DDDDD', customer: 'Anand Restaurant', plan: 'yearly', state: 'Active',
  paidUntil: '2027-06-01T08:00:00Z', revoked: false, deviceBound: true, lastSeenAt: '2026-11-27T08:00:00Z',
  lastVersion: '2.3.3', createdAt: '2026-11-17T07:00:00Z', ...changes
});

function mockApi(list, answer) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    calls.push({ url, method: options.method || 'GET' });
    if (String(url).endsWith('/support-code')) return answer;
    return { ok: true, status: 200, json: async () => list };
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><ShopLicencesPage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('ShopLicencesPage day code', () => {
  it("shows today's support code for the shop PC", async () => {
    const calls = mockApi([licence()], { ok: true, status: 200, json: async () => ({ code: 'ABCD-EFGH', day: '2026-11-27', deviceCode: 'DEV-0000' }) });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Day code' }));
    expect(await screen.findByText(/ABCD-EFGH/)).toBeInTheDocument();
    expect(screen.getByText(/DEV-0000/)).toBeInTheDocument();
    expect(screen.getByText(/2026-11-27/)).toBeInTheDocument();
    expect(calls.filter((c) => c.url.endsWith('/support-code'))).toEqual([{ url: '/api/platform/licences/3/support-code', method: 'GET' }]);
  });

  it('shows the server message when no code can be made', async () => {
    mockApi([licence()], { ok: false, status: 400, json: async () => ({ message: 'This licence is not used on a PC yet.' }) });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Day code' }));
    expect(await screen.findByText(/This licence is not used on a PC yet/)).toBeInTheDocument();
    expect(screen.queryByText(/ABCD-EFGH/)).not.toBeInTheDocument();
  });

  it('has no day code for a licence without a PC or a stopped licence', async () => {
    const calls = mockApi([licence({ id: 4, deviceBound: false }), licence({ id: 5, key: 'POS-EEEEE-FFFFF-GGGGG-HHHHH', revoked: true, state: 'Revoked' })],
      { ok: true, status: 200, json: async () => ({ code: 'ABCD-EFGH' }) });
    renderPage();
    const buttons = await screen.findAllByRole('button', { name: 'Day code' });
    expect(buttons).toHaveLength(2);
    buttons.forEach((b) => expect(b).toBeDisabled());
    buttons.forEach((b) => fireEvent.click(b));
    expect(calls.some((c) => c.url.endsWith('/support-code'))).toBe(false);
  });
});
