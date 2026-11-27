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

function mockApi(passwordReply) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    const key = `${options?.method ?? 'GET'} ${url}`;
    calls.push({ key, body: options?.body ? JSON.parse(options.body) : undefined });
    if (key === 'GET /api/auth/2fa') return { ok: true, status: 200, json: async () => ({ enabled: false, recoveryCodesLeft: 0 }) };
    if (key === 'POST /api/auth/password') {
      const [status, body] = passwordReply;
      return { ok: status < 300, status, json: async () => body };
    }
    return { ok: false, status: 404, json: async () => ({}) };
  }));
  return calls;
}

const show = () => render(<I18nProvider initialLang="en"><ToastProvider><SecurityPage /></ToastProvider></I18nProvider>);

const fill = async (current, next, again) => {
  fireEvent.change(await screen.findByLabelText('Current password'), { target: { value: current } });
  fireEvent.change(screen.getByLabelText('New password'), { target: { value: next } });
  fireEvent.change(screen.getByLabelText('Type the new password again'), { target: { value: again } });
  fireEvent.click(screen.getByRole('button', { name: 'Change password' }));
};

describe('Change my password', () => {
  it('sends the current and new password', async () => {
    const calls = mockApi([200, {}]);
    show();
    await fill('Old#2026x', 'mahesh pos', 'mahesh pos');
    expect(await screen.findByText('Password changed. Other devices are signed out.')).toBeInTheDocument();
    const post = calls.find((c) => c.key === 'POST /api/auth/password');
    expect(post.body).toEqual({ currentPassword: 'Old#2026x', newPassword: 'mahesh pos' });
  });

  it('stops when the two new passwords differ', async () => {
    const calls = mockApi([200, {}]);
    show();
    await fill('Old#2026x', 'mahesh pos', 'mahesh pss');
    expect(await screen.findByText('The two new passwords are not the same')).toBeInTheDocument();
    expect(calls.some((c) => c.key === 'POST /api/auth/password')).toBe(false);
  });

  it('shows the server error next to the field', async () => {
    mockApi([400, { errors: { currentPassword: ['The current password is not right.'] } }]);
    show();
    await fill('Wrong', 'mahesh pos', 'mahesh pos');
    await waitFor(() => expect(screen.getByText('The current password is not right.')).toBeInTheDocument());
  });
});
