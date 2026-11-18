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
const updates = (changes = {}) => ({
  available: true, mode: 'auto', pinnedVersion: null, requested: false,
  status: { checkedAt: '2026-11-19T22:00:00Z', trigger: 'nightly', current: '1.7.4', target: '1.7.4', result: 'up-to-date', message: 'Version 1.7.4 is the newest.' },
  ...changes
});

function mockApi(updatesAnswer, runAnswer) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    const method = options.method || 'GET';
    calls.push({ url, method });
    if (url === '/api/licence/updates') return { ok: true, status: 200, json: async () => updatesAnswer };
    if (url === '/api/licence/updates/run') return runAnswer;
    return { ok: true, status: 200, json: async () => active };
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><LicencePage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Licence page, updates', () => {
  it('shows the last update check and updates now after asking', async () => {
    const calls = mockApi(updates(), { ok: true, status: 200, json: async () => updates({ requested: true }) });
    renderPage();
    expect(await screen.findByText('Up to date')).toBeInTheDocument();
    expect(screen.getByText('Version 1.7.4 is the newest.')).toBeInTheDocument();
    expect(screen.getByText('Automatic')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Update now' }));
    expect(screen.getByText('Update this PC now?')).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: 'Update now' }).at(-1));
    expect(await screen.findAllByText('Update asked for. It starts within 2 minutes.')).not.toHaveLength(0);
    expect(calls.filter((c) => c.method === 'POST').map((c) => c.url)).toEqual(['/api/licence/updates/run']);
    expect(screen.getByRole('button', { name: 'Update now' })).toBeDisabled();
  });

  it('cannot update when support paused updates', async () => {
    mockApi(updates({ mode: 'paused', status: null }));
    renderPage();
    expect(await screen.findByText('Support has paused updates for this PC.')).toBeInTheDocument();
    expect(screen.getByText('Not checked yet. The PC checks every night at 03:30.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Update now' })).toBeDisabled();
  });

  it('shows nothing about updates on the cloud server', async () => {
    mockApi({ available: false, mode: 'auto', requested: false, status: null });
    renderPage();
    expect(await screen.findByText('Anand Restaurant')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Update now' })).not.toBeInTheDocument();
  });
});
