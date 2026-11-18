// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import ShopLicencesPage from './ShopLicencesPage';

const licence = (changes = {}) => ({
  id: 3, key: 'POS-AAAAA-BBBBB-CCCCC-DDDDD', customer: 'Anand Restaurant', plan: 'trial', state: 'Trial',
  paidUntil: '2026-12-17T08:00:00Z', revoked: false, deviceBound: true, lastSeenAt: '2026-11-17T08:00:00Z',
  lastVersion: '1.7.0', createdAt: '2026-11-17T07:00:00Z', ...changes
});

function mockApi(list) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
    if (!options.method) return { ok: true, status: 200, json: async () => list };
    return { ok: true, status: 200, json: async () => ({}) };
  }));
  return calls;
}

const posts = (calls) => calls.filter((c) => c.method === 'POST');
const renderPage = () => render(<I18nProvider><ToastProvider><ShopLicencesPage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Shop licences page', () => {
  it('pauses or pins the updates of one shop', async () => {
    const calls = mockApi([licence({ updateMode: 'auto' })]);
    renderPage();
    expect(await screen.findByText('Automatic')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Updates' }));
    fireEvent.change(screen.getByLabelText('Updates'), { target: { value: 'pinned' } });
    fireEvent.change(screen.getByLabelText('Version to keep'), { target: { value: 'one' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Enter a version like 1.7.3.')).toBeInTheDocument();
    expect(posts(calls)).toEqual([]);

    fireEvent.change(screen.getByLabelText('Version to keep'), { target: { value: 'v1.7.3' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Updates for Anand Restaurant saved. The shop PC gets it with its next licence renewal.')).toBeInTheDocument();
    expect(posts(calls)).toEqual([{ url: '/api/platform/licences/3/updates', method: 'POST', body: { mode: 'pinned', version: '1.7.3' } }]);
  });

  it('lists the licences with key, version and status', async () => {
    mockApi([licence()]);
    renderPage();
    expect(await screen.findByText('Anand Restaurant')).toBeInTheDocument();
    expect(screen.getByText('POS-AAAAA-BBBBB-CCCCC-DDDDD')).toBeInTheDocument();
    expect(screen.getByText('1.7.0')).toBeInTheDocument();
  });

  it('shows an empty message', async () => {
    mockApi([]);
    renderPage();
    expect(await screen.findByText('No licences yet.')).toBeInTheDocument();
  });

  it('creates a licence', async () => {
    const calls = mockApi([]);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'New licence' }));
    fireEvent.change(screen.getByLabelText('Customer name'), { target: { value: ' Hotel Rasoi ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));
    expect(await screen.findByText('Licence created.')).toBeInTheDocument();
    expect(posts(calls)).toEqual([{ url: '/api/platform/licences', method: 'POST', body: { customer: 'Hotel Rasoi', plan: 'trial', days: null } }]);
  });

  it('asks for the customer name and a sensible number of days', async () => {
    const calls = mockApi([]);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'New licence' }));
    fireEvent.change(screen.getByLabelText('Days (optional)'), { target: { value: '5000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));
    expect(await screen.findByText('Enter the customer name.')).toBeInTheDocument();
    expect(screen.getByText('Enter a number of days from 1 to 1100.')).toBeInTheDocument();
    expect(posts(calls)).toHaveLength(0);
  });

  it('extends a trial into a yearly licence', async () => {
    const calls = mockApi([licence()]);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Extend' }));
    expect(screen.getByLabelText('Days to add')).toHaveValue('365');
    fireEvent.click(screen.getByRole('button', { name: 'Extend licence' }));
    expect(await screen.findByText('Licence extended.')).toBeInTheDocument();
    expect(posts(calls)).toEqual([{ url: '/api/platform/licences/3/extend', method: 'POST', body: { days: 365, plan: 'yearly' } }]);
  });

  it('stops a licence only after the owner confirms', async () => {
    const calls = mockApi([licence()]);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Stop' }));
    expect(posts(calls)).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Yes, stop it' }));
    await waitFor(() => expect(posts(calls)).toEqual([{ url: '/api/platform/licences/3/revoke', method: 'POST', body: null }]));
    expect(await screen.findByText('Anand Restaurant is stopped.')).toBeInTheDocument();
  });

  it('restores a stopped licence', async () => {
    const calls = mockApi([licence({ revoked: true, state: 'Revoked' })]);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Restore' }));
    await waitFor(() => expect(posts(calls)).toEqual([{ url: '/api/platform/licences/3/restore', method: 'POST', body: null }]));
  });

  it('frees the key for a new PC after confirming', async () => {
    const calls = mockApi([licence()]);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Reset PC' }));
    fireEvent.click(screen.getByRole('button', { name: 'Yes, reset' }));
    await waitFor(() => expect(posts(calls)).toEqual([{ url: '/api/platform/licences/3/reset-device', method: 'POST', body: null }]));
  });

  it('hides Reset PC when no PC uses the key yet', async () => {
    mockApi([licence({ deviceBound: false })]);
    renderPage();
    await screen.findByText('Anand Restaurant');
    expect(screen.queryByRole('button', { name: 'Reset PC' })).not.toBeInTheDocument();
  });
});
