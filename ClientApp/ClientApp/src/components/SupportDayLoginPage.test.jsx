// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import SupportDayLoginPage from './SupportDayLoginPage';

function mockApi(answer) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
    return answer;
  }));
  return calls;
}

const renderPage = () => render(
  <MemoryRouter initialEntries={['/support-day-login']}>
    <I18nProvider>
      <Routes>
        <Route path="/support-day-login" element={<SupportDayLoginPage />} />
        <Route path="/login" element={<p>Sign-in page</p>} />
      </Routes>
    </I18nProvider>
  </MemoryRouter>,
);

const submit = (code) => {
  if (code !== undefined) fireEvent.change(screen.getByLabelText("Today's code"), { target: { value: code } });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in as support' }));
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('SupportDayLoginPage', () => {
  it('asks for the code before sending anything', async () => {
    const calls = mockApi({ ok: true, status: 200, json: async () => ({}) });
    renderPage();
    submit();
    expect(await screen.findByText('Enter the code from support.')).toBeInTheDocument();
    expect(calls).toHaveLength(0);
  });

  it('treats a blank code as no code', async () => {
    const calls = mockApi({ ok: true, status: 200, json: async () => ({}) });
    renderPage();
    submit('    ');
    expect(await screen.findByText('Enter the code from support.')).toBeInTheDocument();
    expect(calls).toHaveLength(0);
  });

  it('sends the trimmed code', async () => {
    const calls = mockApi({ ok: true, status: 200, json: async () => ({ tenantId: 1 }) });
    renderPage();
    submit('  abcd-efgh ');
    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0]).toEqual({ url: '/api/auth/support-day-login', method: 'POST', body: { code: 'abcd-efgh' } });
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows the server message for a wrong code', async () => {
    mockApi({ ok: false, status: 400, json: async () => ({ message: "The code is wrong or not for today. Ask support for today's code." }) });
    renderPage();
    submit('ABCD-EFGH');
    expect(await screen.findByRole('alert')).toHaveTextContent('The code is wrong or not for today.');
  });

  it('shows the server message when support access is switched off', async () => {
    mockApi({ ok: false, status: 400, json: async () => ({ message: 'This restaurant has turned off support access.' }) });
    renderPage();
    submit('ABCD-EFGH');
    expect(await screen.findByRole('alert')).toHaveTextContent('This restaurant has turned off support access.');
  });

  it('shows an error when the server cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('offline'); }));
    renderPage();
    submit('ABCD-EFGH');
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('links back to the sign-in page', () => {
    mockApi({ ok: true, status: 200, json: async () => ({}) });
    renderPage();
    fireEvent.click(screen.getByRole('link', { name: 'Back to sign-in' }));
    expect(screen.getByText('Sign-in page')).toBeInTheDocument();
  });
});
