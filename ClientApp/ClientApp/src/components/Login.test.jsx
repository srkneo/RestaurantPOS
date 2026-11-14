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
});
