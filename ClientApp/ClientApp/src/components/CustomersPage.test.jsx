// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import CustomersPage from './CustomersPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const asha = { id: 1, name: 'Asha', phone: '9825012345', points: 12, visits: 3, spent: 1250, lastVisitAt: '2026-11-19T10:00:00Z', consentAt: '2026-11-01T10:00:00Z' };

const mock = (handler) => {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    const call = { url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null };
    calls.push(call);
    const [status, body] = handler(call);
    return { ok: status < 300, status, json: async () => body };
  }));
  return calls;
};

const show = () => render(<I18nProvider><ToastProvider><CustomersPage /></ToastProvider></I18nProvider>);

describe('Loyalty guests page', () => {
  it('lists guests with their points', async () => {
    mock(() => [200, [asha]]);
    show();
    expect(await screen.findByText('Asha')).toBeInTheDocument();
    expect(screen.getByText('9825012345')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
  });

  it('adds a guest only after the consent box is ticked', async () => {
    const calls = mock((c) => (c.method === 'POST'
      ? [201, { id: 2, name: 'Ravi', phone: '9000000000', points: 0, visits: 0, spent: 0, lastVisitAt: null, consentAt: '2026-11-20T10:00:00Z' }]
      : [200, []]));
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Add guest' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Ravi' } });
    fireEvent.change(screen.getByLabelText('Mobile number'), { target: { value: '9000000000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Ask the guest first and tick the box.')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST')).toBe(false);

    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true));
    expect(calls.find((c) => c.method === 'POST').body).toEqual({ name: 'Ravi', phone: '9000000000', consent: true });
  });
});
