// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from '../AuthContext';
import { I18nProvider } from '../i18n/I18nContext';
import SignupPage from './SignupPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/signup']}>
      <I18nProvider><AuthProvider>
        <Routes>
          <Route path="/signup" element={<SignupPage />} />
          <Route path="/admin/setup" element={<h1>Setup checklist</h1>} />
        </Routes>
      </AuthProvider></I18nProvider>
    </MemoryRouter>,
  );
}

describe('Signup', () => {
  it('shows the plans, sends the form and signs the owner in', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      calls.push({ url, body: options?.body ? JSON.parse(options.body) : null });
      if (url === '/api/signup/plans') return { ok: true, status: 200, json: async () => ({ trialDays: 30, monthly: 1000, yearly: 10000, signupOpen: true }) };
      if (url === '/api/signup') return { ok: true, status: 200, json: async () => ({ trialUntil: '2026-12-19' }) };
      if (url === '/api/auth/login') return { ok: true, status: 200, json: async () => { throw new Error('empty'); } };
      if (url === '/api/auth/me') return { ok: true, status: 200, json: async () => ({ email: 'kiran@cafe.local', roles: ['admin'] }) };
      return { ok: false, status: 404, json: async () => null };
    }));
    renderPage();
    expect(await screen.findByText(/30 days free/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Business name'), { target: { value: 'Patel Cafe' } });
    fireEvent.change(screen.getByLabelText('Your name'), { target: { value: 'Kiran Patel' } });
    fireEvent.change(screen.getByLabelText('Email (you sign in with it)'), { target: { value: 'kiran@cafe.local' } });
    fireEvent.change(screen.getByLabelText('Mobile number'), { target: { value: '9825012345' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'Owner#2026xy' } });
    fireEvent.click(screen.getByRole('button', { name: 'Start free trial' }));
    expect(await screen.findByRole('heading', { name: 'Setup checklist' })).toBeInTheDocument();
    const sent = calls.find((c) => c.url === '/api/signup').body;
    expect(sent).toMatchObject({ restaurantName: 'Patel Cafe', ownerPhone: '9825012345', website: '' });
  });

  it('shows the server error next to the field', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      if (url === '/api/signup/plans') return { ok: true, status: 200, json: async () => ({ trialDays: 30, monthly: 1000, yearly: 10000, signupOpen: true }) };
      if (url === '/api/signup') return { ok: false, status: 400, json: async () => ({ errors: { ownerphone: ['Enter a 10-digit mobile number.'] } }) };
      return { ok: false, status: 401, json: async () => null };
    }));
    renderPage();
    await screen.findByText(/30 days free/);
    fireEvent.click(screen.getByRole('button', { name: 'Start free trial' }));
    expect(await screen.findByText('Enter a 10-digit mobile number.')).toBeInTheDocument();
  });
});
