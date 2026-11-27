// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import PlatformPage from './PlatformPage';

const TENANTS = [
  { id: 1, name: 'My Restaurant', isActive: true, createdAt: '2026-11-14T00:00:00Z', outletCount: 1, staffCount: 4, lastOrderAt: '2026-11-14T10:30:00Z' },
  { id: 2, name: 'Anand Thali House', isActive: false, createdAt: '2026-11-14T00:00:00Z', outletCount: 1, staffCount: 1, lastOrderAt: null },
];
const realLocation = window.location;

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  Object.defineProperty(window, 'location', { value: realLocation, writable: true, configurable: true });
});

describe('Platform support login', () => {
  it('opens an active restaurant as support after confirming', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      calls.push({ url, method: options?.method ?? 'GET' });
      if (options?.method) return { ok: true, status: 200, json: async () => ({ tenantId: 1 }) };
      return { ok: true, json: async () => TENANTS };
    }));
    Object.defineProperty(window, 'location', { value: { ...realLocation, assign: vi.fn() }, writable: true, configurable: true });
    render(<I18nProvider><ToastProvider><PlatformPage /></ToastProvider></I18nProvider>);

    const row = (await screen.findByText('My Restaurant')).closest('tr');
    fireEvent.click(within(row).getByRole('button', { name: 'Support login' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Open this restaurant as Support?')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Open as support' }));
    await waitFor(() => expect(calls.some((c) => c.url === '/api/platform/tenants/1/support-login' && c.method === 'POST')).toBe(true));
    await waitFor(() => expect(window.location.assign).toHaveBeenCalledWith('/'));

    const stopped = screen.getByText('Anand Thali House').closest('tr');
    expect(within(stopped).queryByRole('button', { name: 'Support login' })).not.toBeInTheDocument();
  });
});
