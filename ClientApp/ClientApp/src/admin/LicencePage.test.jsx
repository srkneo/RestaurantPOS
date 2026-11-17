// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import LicencePage from './LicencePage';

const active = {
  state: 'Active', locked: false, required: true, customer: 'Anand Restaurant', plan: 'yearly',
  paidUntil: '2027-11-17T08:00:00Z', leaseUntil: '2026-11-24T08:00:00Z', message: 'Licence active.'
};
const missing = { state: 'Missing', locked: true, required: true, message: 'This PC has no licence yet. Enter the licence key.' };

function mockApi(first, onPost) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
    if (!options.method) return { ok: true, status: 200, json: async () => first };
    return onPost ? onPost(url) : { ok: true, status: 200, json: async () => active };
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><LicencePage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Licence page', () => {
  it('shows the licence of an active shop PC', async () => {
    mockApi(active);
    renderPage();
    expect(await screen.findByText('Anand Restaurant')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Yearly')).toBeInTheDocument();
    expect(screen.getByText('Licence active.')).toBeInTheDocument();
  });

  it('says no licence is needed on the cloud server', async () => {
    mockApi({ state: 'Active', locked: false, required: false, message: 'Licence not required.' });
    renderPage();
    expect(await screen.findByText('This server needs no licence.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Licence key')).not.toBeInTheDocument();
  });

  it('activates with the key that was typed', async () => {
    const calls = mockApi(missing);
    renderPage();
    fireEvent.change(await screen.findByLabelText('Licence key'), { target: { value: ' POS-AAAAA-BBBBB-CCCCC-DDDDD ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Activate' }));
    expect(await screen.findByText('Licence activated.')).toBeInTheDocument();
    expect(calls).toContainEqual({ url: '/api/licence/activate', method: 'POST', body: { key: 'POS-AAAAA-BBBBB-CCCCC-DDDDD' } });
    expect(await screen.findByText('Anand Restaurant')).toBeInTheDocument();
  });

  it('asks for the key when it is empty', async () => {
    const calls = mockApi(missing);
    renderPage();
    await screen.findByLabelText('Licence key');
    fireEvent.click(screen.getByRole('button', { name: 'Activate' }));
    expect(await screen.findByText('Enter the licence key.')).toBeInTheDocument();
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(0);
  });

  it('shows the reason when the licence server refuses the key', async () => {
    mockApi(missing, () => ({ ok: false, status: 400, json: async () => ({ message: 'This licence key is not known. Check the key and try again.' }) }));
    renderPage();
    fireEvent.change(await screen.findByLabelText('Licence key'), { target: { value: 'POS-WRONG' } });
    fireEvent.click(screen.getByRole('button', { name: 'Activate' }));
    expect(await screen.findByText(/This licence key is not known\./)).toBeInTheDocument();
  });

  it('refreshes the licence now', async () => {
    const calls = mockApi(active);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Refresh now' }));
    expect(await screen.findByText('Licence refreshed.')).toBeInTheDocument();
    expect(calls.some((c) => c.url === '/api/licence/refresh' && c.method === 'POST')).toBe(true);
  });
});
