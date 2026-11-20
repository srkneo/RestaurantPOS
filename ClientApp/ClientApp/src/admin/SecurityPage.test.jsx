// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import SecurityPage from './SecurityPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function mockApi(routes) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    const key = `${options?.method ?? 'GET'} ${url}`;
    calls.push({ key, body: options?.body ? JSON.parse(options.body) : undefined });
    const route = routes[key];
    if (!route) return { ok: false, status: 404, json: async () => ({}) };
    const [status, body] = route();
    return { ok: status < 300, status, json: async () => body };
  }));
  return calls;
}

const show = () => render(<I18nProvider initialLang="en"><ToastProvider><SecurityPage /></ToastProvider></I18nProvider>);

describe('Sign-in security page', () => {
  it('turns on the authenticator app and shows the backup codes once', async () => {
    let enabled = false;
    const calls = mockApi({
      'GET /api/auth/2fa': () => [200, { enabled, recoveryCodesLeft: enabled ? 10 : 0 }],
      'POST /api/auth/2fa/setup': () => [200, { key: 'ABCD EFGH IJKL MNOP', uri: 'otpauth://totp/x', qrSvg: '<svg></svg>' }],
      'POST /api/auth/2fa/enable': () => (calls.at(-1).body.code === '123456'
        ? (enabled = true, [200, { recoveryCodes: ['aaaaa-bbbbb', 'ccccc-ddddd'] }])
        : [400, { errors: { code: ['That code is not right. Type the 6 digits the app shows now.'] } }]),
    });
    show();
    expect(await screen.findByText('Off')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Turn on' }));
    expect(await screen.findByText('ABCD EFGH IJKL MNOP')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'QR code for the authenticator app' })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('6-digit code (or a backup code)'), { target: { value: '000000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm and turn on' }));
    expect(await screen.findByText('That code is not right. Type the 6 digits the app shows now.')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('6-digit code (or a backup code)'), { target: { value: '123456' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm and turn on' }));
    expect(await screen.findByText('aaaaa-bbbbb')).toBeInTheDocument();
    expect(await screen.findByText('On. Backup codes left: 10')).toBeInTheDocument();
  });

  it('asks before turning it off and sends the code', async () => {
    const calls = mockApi({
      'GET /api/auth/2fa': () => [200, { enabled: true, recoveryCodesLeft: 7 }],
      'POST /api/auth/2fa/disable': () => [204, null],
    });
    show();
    expect(await screen.findByText('On. Backup codes left: 7')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Turn off' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('6-digit code (or a backup code)'), { target: { value: '654321' } });
    fireEvent.click(screen.getByRole('button', { name: 'Turn off' }));
    expect(calls.some((c) => c.key === 'POST /api/auth/2fa/disable')).toBe(false);
    fireEvent.click(await screen.findByRole('button', { name: 'Yes, turn off' }));
    await waitFor(() => expect(calls.find((c) => c.key === 'POST /api/auth/2fa/disable')?.body).toEqual({ code: '654321' }));
  });
});
