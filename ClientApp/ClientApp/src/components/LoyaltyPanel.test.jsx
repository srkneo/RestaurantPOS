// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import LoyaltyPanel from './LoyaltyPanel';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const GUEST = { id: 1, name: 'Asha', phone: '9825012345', points: 120 };

function mockApi({ settings = { enabled: true, rupeesPerPoint: 100, pointValue: 1 }, settingsStatus = 200, guest = GUEST, join } = {}) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    const method = options?.method ?? 'GET';
    calls.push({ method, url, body: options?.body ? JSON.parse(options.body) : undefined });
    if (url === '/api/customers/settings') return { ok: settingsStatus < 300, status: settingsStatus, json: async () => settings };
    if (url.startsWith('/api/customers/by-phone')) {
      return guest ? { ok: true, status: 200, json: async () => guest } : { ok: false, status: 404, json: async () => ({}) };
    }
    if (method === 'POST' && url === '/api/customers') return join ?? { ok: true, status: 201, json: async () => ({ ...GUEST, points: 0 }) };
    return { ok: false, status: 404, json: async () => ({}) };
  }));
  return calls;
}

const show = (props = {}) => render(
  <I18nProvider initialLang="en">
    <ToastProvider>
      <LoyaltyPanel phone="9825012345" name="Asha" due={500} canUse busy={false} refreshKey={1} onUse={() => {}} {...props} />
    </ToastProvider>
  </I18nProvider>
);

describe('Loyalty panel on the bill', () => {
  it('caps the points at what is due', async () => {
    mockApi({ settings: { enabled: true, rupeesPerPoint: 100, pointValue: 0.5 } });
    const onUse = vi.fn();
    show({ due: 30, onUse });
    fireEvent.click(await screen.findByRole('button', { name: 'Use 60 points (₹30.00)' }));
    expect(onUse).toHaveBeenCalledWith(30);
  });

  it('uses all points when they are worth less than the due', async () => {
    mockApi();
    const onUse = vi.fn();
    show({ onUse });
    fireEvent.click(await screen.findByRole('button', { name: 'Use 120 points (₹120.00)' }));
    expect(onUse).toHaveBeenCalledWith(120);
  });

  it('shows points but no Use button on a paid bill, or with no points, or a due under one point', async () => {
    mockApi();
    show({ canUse: false });
    expect(await screen.findByText('Asha: 120 points')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Use / })).not.toBeInTheDocument();
    cleanup();

    mockApi({ guest: { ...GUEST, points: 0 } });
    show();
    expect(await screen.findByText('Asha: 0 points')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Use / })).not.toBeInTheDocument();
    cleanup();

    mockApi({ settings: { enabled: true, rupeesPerPoint: 100, pointValue: 2 } });
    show({ due: 1 });
    expect(await screen.findByText('Asha: 120 points')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Use / })).not.toBeInTheDocument();
  });

  it('shows nothing without a phone, when loyalty is off, or when settings cannot be read', async () => {
    let calls = mockApi();
    const { container } = show({ phone: '' });
    await waitFor(() => expect(calls.some((c) => c.url === '/api/customers/settings')).toBe(true));
    expect(calls.some((c) => c.url.startsWith('/api/customers/by-phone'))).toBe(false);
    expect(container.querySelector('.pos-bill-loyalty')).toBeNull();
    cleanup();

    calls = mockApi({ settings: { enabled: false, rupeesPerPoint: 100, pointValue: 1 } });
    const off = show();
    await waitFor(() => expect(calls.some((c) => c.url === '/api/customers/settings')).toBe(true));
    expect(off.container.querySelector('.pos-bill-loyalty')).toBeNull();
    cleanup();

    calls = mockApi({ settingsStatus: 500 });
    const broken = show();
    await waitFor(() => expect(calls.some((c) => c.url === '/api/customers/settings')).toBe(true));
    expect(broken.container.querySelector('.pos-bill-loyalty')).toBeNull();
  });

  it('shows the server message when joining fails, and Cancel closes the form', async () => {
    mockApi({
      guest: null,
      join: { ok: false, status: 400, json: async () => ({ errors: { phone: ['A guest with this mobile number is already in the list.'] } }) },
    });
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Add to loyalty' }));
    expect(screen.getByLabelText('Name')).toHaveValue('Asha');
    fireEvent.click(screen.getByLabelText(/The guest agreed/));
    fireEvent.click(screen.getByRole('button', { name: 'Add to loyalty' }));
    expect(await screen.findByText('A guest with this mobile number is already in the list.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.getByText('Not a loyalty guest yet.')).toBeInTheDocument();
  });

  it('shows a network message when the server cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      if (url === '/api/customers/settings') return { ok: true, status: 200, json: async () => ({ enabled: true, rupeesPerPoint: 100, pointValue: 1 }) };
      if (url.startsWith('/api/customers/by-phone')) return { ok: false, status: 404, json: async () => ({}) };
      if (options?.method === 'POST') throw new Error('offline');
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Add to loyalty' }));
    fireEvent.click(screen.getByLabelText(/The guest agreed/));
    fireEvent.click(screen.getByRole('button', { name: 'Add to loyalty' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Add to loyalty' })).not.toBeDisabled());
    expect(screen.queryByText('Not a loyalty guest yet.')).not.toBeInTheDocument();
  });
});
