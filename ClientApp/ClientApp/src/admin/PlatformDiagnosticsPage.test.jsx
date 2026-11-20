// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import PlatformDiagnosticsPage from './PlatformDiagnosticsPage';

const sample = {
  version: '2.3.0', environment: 'Production', startedAtUtc: '2026-11-21T00:00:00Z', uptimeSeconds: 3 * 3600 + 25 * 60,
  databaseOk: true, sentryOn: false, telemetryOn: true, restaurants: 4, failedSignIns24h: 2,
  recentEvents: [
    { atUtc: '2026-11-21T02:00:00Z', tenantId: null, userId: 'u1', type: 'sign-in', path: '/api/auth/login', status: 401, success: false, ip: '1.2.3.4', requestId: 'r1', targetId: null },
    { atUtc: '2026-11-21T02:01:00Z', tenantId: 1, userId: 'u1', type: 'staff-update', path: '/api/staff/x', status: 200, success: true, ip: '1.2.3.4', requestId: 'r2', targetId: 'x' },
  ],
};

const mock = (answer) => {
  const fn = vi.fn(async () => answer);
  vi.stubGlobal('fetch', fn);
  return fn;
};

const renderPage = () => render(<I18nProvider><PlatformDiagnosticsPage /></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Platform diagnostics page', () => {
  it('shows version, uptime, monitoring switches and recent events', async () => {
    const fn = mock({ ok: true, status: 200, json: async () => sample });
    renderPage();
    expect(await screen.findByText('2.3.0')).toBeInTheDocument();
    expect(fn).toHaveBeenCalledWith('/api/platform/diagnostics');
    expect(screen.getByText('3 h 25 min')).toBeInTheDocument();
    expect(screen.getByText('Off')).toBeInTheDocument();
    expect(screen.getByText('On')).toBeInTheDocument();
    expect(screen.getByText('sign-in')).toBeInTheDocument();
    expect(screen.getByText('staff-update')).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
  });

  it('says when there are no events yet', async () => {
    mock({ ok: true, status: 200, json: async () => ({ ...sample, recentEvents: [] }) });
    renderPage();
    expect(await screen.findByText('No security events yet.')).toBeInTheDocument();
  });

  it('shows an error with a retry when the server refuses', async () => {
    const fn = mock({ ok: false, status: 403, json: async () => ({}) });
    renderPage();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: /try again|retry/i })[0]);
    expect(fn).toHaveBeenCalledTimes(2);
  });
});
