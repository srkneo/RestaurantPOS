// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import ResetPasswordPage from './ResetPasswordPage';

function mockApi(answer) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
    return answer;
  }));
  return calls;
}

const renderPage = () => render(
  <MemoryRouter initialEntries={['/reset-password']}>
    <I18nProvider>
      <ToastProvider>
        <Routes>
          <Route path="/reset-password" element={<ResetPasswordPage />} />
          <Route path="/login" element={<p>Sign-in page</p>} />
        </Routes>
      </ToastProvider>
    </I18nProvider>
  </MemoryRouter>,
);

const fill = (values) => {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: values.email } });
  fireEvent.change(screen.getByLabelText('Reset code'), { target: { value: values.code } });
  fireEvent.change(screen.getByLabelText('New password'), { target: { value: values.password } });
  fireEvent.change(screen.getByLabelText('New password again'), { target: { value: values.repeat } });
  fireEvent.click(screen.getByRole('button', { name: 'Change password' }));
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Reset password page', () => {
  it('changes the password with the code from support and goes back to sign-in', async () => {
    const calls = mockApi({ ok: true, status: 200, json: async () => ({ message: 'Password changed.' }) });
    renderPage();
    fill({ email: ' owner@shop.test ', code: 'abcd-efgh', password: 'New#Pass2026', repeat: 'New#Pass2026' });
    expect(await screen.findByText('Sign-in page')).toBeInTheDocument();
    expect(calls).toEqual([{ url: '/api/auth/owner-reset', method: 'POST', body: { email: 'owner@shop.test', code: 'abcd-efgh', newPassword: 'New#Pass2026' } }]);
  });

  it('checks the form before sending', async () => {
    const calls = mockApi({ ok: true, status: 200, json: async () => ({}) });
    renderPage();
    fill({ email: 'owner@shop.test', code: 'ABCD-EFGH', password: 'New#Pass2026', repeat: 'Other#Pass2026' });
    expect(await screen.findByText('The two passwords are not the same.')).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it('shows why the server said no', async () => {
    mockApi({ ok: false, status: 400, json: async () => ({ message: 'The email or the code is not right, or the code has expired. Ask support for a new code.' }) });
    renderPage();
    fill({ email: 'owner@shop.test', code: 'WRONG', password: 'New#Pass2026', repeat: 'New#Pass2026' });
    expect(await screen.findByRole('alert')).toHaveTextContent('Ask support for a new code.');
  });
});
