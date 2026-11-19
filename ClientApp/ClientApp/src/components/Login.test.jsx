// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { AuthProvider } from '../AuthContext';
import { I18nProvider } from '../i18n/I18nContext';
import Login from './Login';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Login', () => {
  it('shows an error next to the form for a wrong password', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: false, json: async () => null })));
    render(<MemoryRouter><I18nProvider><AuthProvider><Login /></AuthProvider></I18nProvider></MemoryRouter>);

    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'cashier@test.local' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'wrong-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password');
  });

  it('asks the platform owner for the Telegram code', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      calls.push({ url, body: options?.body ? JSON.parse(options.body) : null });
      if (url === '/api/auth/login') return { ok: true, status: 200, json: async () => ({ twoStep: true, challenge: 'c1' }) };
      if (url === '/api/auth/login/code') return { ok: calls.filter((c) => c.url === url).length > 1, status: 401, json: async () => null };
      return { ok: false, status: 401, json: async () => null };
    }));
    render(<MemoryRouter><I18nProvider><AuthProvider><Login /></AuthProvider></I18nProvider></MemoryRouter>);

    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'owner@pos.local' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'Platform#2026x' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    const codeBox = await screen.findByLabelText('Code from Telegram');
    fireEvent.change(codeBox, { target: { value: '12a3456' } });
    fireEvent.click(screen.getByRole('button', { name: 'Verify' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('The code is wrong or has expired.');
    expect(calls.find((c) => c.url === '/api/auth/login/code').body).toEqual({ challenge: 'c1', code: '123456' });
  });
});
