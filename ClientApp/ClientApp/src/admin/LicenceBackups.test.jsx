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
const backups = (changes = {}) => ({
  available: true, checkedAt: '2026-11-19T21:30:00Z', backupAt: '2026-11-19T21:30:00Z', lastCopiedAt: '2026-11-19T21:30:00Z',
  target: 'USB disk', result: 'copied', message: 'Copied pos-20261120-030000.dump to the USB disk.', warning: false, requested: false, ...changes
});

function mockApi(backupAnswer) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    const method = options.method || 'GET';
    calls.push({ url, method });
    if (url === '/api/licence/backups') return { ok: true, status: 200, json: async () => backupAnswer };
    if (url === '/api/licence/backups/run') return { ok: true, status: 200, json: async () => ({ ...backupAnswer, requested: true }) };
    if (url === '/api/licence/updates') return { ok: true, status: 200, json: async () => ({ available: false }) };
    return { ok: true, status: 200, json: async () => active };
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><LicencePage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Licence page, backups', () => {
  it('shows the last copy and backs up now', async () => {
    const calls = mockApi(backups());
    renderPage();
    expect(await screen.findByText('Copied pos-20261120-030000.dump to the USB disk.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Back up now' }));
    expect(await screen.findAllByText('Backup asked for. It runs within 2 minutes; keep the USB disk plugged in.')).not.toHaveLength(0);
    expect(calls.filter((c) => c.method === 'POST').map((c) => c.url)).toEqual(['/api/licence/backups/run']);
    expect(screen.getByRole('button', { name: 'Back up now' })).toBeDisabled();
  });

  it('warns when no copy was made for days', async () => {
    mockApi(backups({ warning: true, result: 'no-disk', lastCopiedAt: '2026-11-10T21:30:00Z', message: 'No backup disk found. Plug in the USB disk named POSBACKUP.' }));
    renderPage();
    expect(await screen.findByText('The last copy off this PC is too old. Plug in the backup USB disk, then press Back up now.')).toBeInTheDocument();
  });

  it('shows nothing about backups on the cloud server', async () => {
    mockApi({ available: false });
    renderPage();
    expect(await screen.findByText('Anand Restaurant')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Back up now' })).not.toBeInTheDocument();
  });
});
