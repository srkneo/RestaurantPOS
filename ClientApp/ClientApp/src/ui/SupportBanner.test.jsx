// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { AuthProvider } from '../AuthContext';
import SupportBanner from './SupportBanner';

const realLocation = window.location;

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  Object.defineProperty(window, 'location', { value: realLocation, writable: true, configurable: true });
});

const show = (roles) => {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, method: options?.method ?? 'GET' });
    if (url === '/api/auth/me') return { ok: true, status: 200, json: async () => ({ displayName: 'Support (platform)', roles, tenantId: '7', outletId: '7' }) };
    return { ok: true, status: 200, json: async () => ({}) };
  }));
  Object.defineProperty(window, 'location', { value: { ...realLocation, assign: vi.fn() }, writable: true, configurable: true });
  render(<MemoryRouter><I18nProvider><AuthProvider><SupportBanner /></AuthProvider></I18nProvider></MemoryRouter>);
  return calls;
};

describe('Support session banner', () => {
  it('shows a red banner during a support session and ends it', async () => {
    const calls = show(['admin', 'support']);
    expect(await screen.findByText('Support session: every change is logged. Payments, refunds, voids and password changes are blocked.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'End support session' }));
    await waitFor(() => expect(calls.some((c) => c.url === '/api/auth/logout' && c.method === 'POST')).toBe(true));
    await waitFor(() => expect(window.location.assign).toHaveBeenCalledWith('/login'));
  });

  it('shows nothing for normal staff', async () => {
    const calls = show(['admin']);
    await waitFor(() => expect(calls.some((c) => c.url === '/api/auth/me')).toBe(true));
    expect(screen.queryByRole('button', { name: 'End support session' })).not.toBeInTheDocument();
  });
});
