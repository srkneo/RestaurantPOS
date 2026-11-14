// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import StaffPage from './StaffPage';

const STAFF = [
  { id: 'a1', email: 'owner@test.local', displayName: 'Owner', role: 'admin', isActive: true, isDevice: false },
  { id: 'k1', email: 'kitchen-screen@test.local', displayName: 'Kitchen screen', role: 'kitchen', isActive: false, isDevice: true },
];

function mockApi(postResponse = { ok: true, status: 201, json: async () => ({}) }) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (options?.method) return postResponse;
    return { ok: true, json: async () => STAFF };
  }));
  return calls;
}

function renderPage() {
  render(<I18nProvider><ToastProvider><StaffPage /></ToastProvider></I18nProvider>);
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Staff page', () => {
  it('lists staff with role, type and status', async () => {
    mockApi();
    renderPage();
    const row = (await screen.findByText('kitchen-screen@test.local')).closest('tr');
    expect(within(row).getByText('Kitchen')).toBeInTheDocument();
    expect(within(row).getByText('Device')).toBeInTheDocument();
    expect(within(row).getByText('Inactive')).toBeInTheDocument();
  });

  it('adds a staff member', async () => {
    const calls = mockApi();
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Add staff' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Asha' } });
    fireEvent.change(within(dialog).getByLabelText('Email'), { target: { value: 'asha@test.local' } });
    fireEvent.change(within(dialog).getByLabelText('Password'), { target: { value: 'Staff#2026x' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Staff member added.')).toBeInTheDocument();
    const post = calls.find((c) => c.options?.method === 'POST');
    expect(post.url).toBe('/api/staff');
    expect(JSON.parse(post.options.body)).toEqual({ displayName: 'Asha', email: 'asha@test.local', role: 'cashier', password: 'Staff#2026x', isDevice: false });
  });

  it('a kitchen device is always given the kitchen role', async () => {
    const calls = mockApi();
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Add staff' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByLabelText('Kitchen screen device'));
    expect(within(dialog).getByLabelText('Role')).toHaveValue('kitchen');
    expect(within(dialog).getByLabelText('Role')).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Screen 1' } });
    fireEvent.change(within(dialog).getByLabelText('Email'), { target: { value: 'screen1@test.local' } });
    fireEvent.change(within(dialog).getByLabelText('Password'), { target: { value: 'Staff#2026x' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

    await screen.findByText('Staff member added.');
    const body = JSON.parse(calls.find((c) => c.options?.method === 'POST').options.body);
    expect(body.role).toBe('kitchen');
    expect(body.isDevice).toBe(true);
  });

  it('checks required fields before sending', async () => {
    const calls = mockApi();
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Add staff' }));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Save' }));

    expect(screen.getByText('Enter a name.')).toBeInTheDocument();
    expect(screen.getByText('Enter an email.')).toBeInTheDocument();
    expect(screen.getByText('Enter a password.')).toBeInTheDocument();
    expect(calls.some((c) => c.options?.method)).toBe(false);
  });

  it('shows the server error next to the field', async () => {
    mockApi({ ok: false, status: 400, json: async () => ({ errors: { password: ['Passwords must have at least one digit (\'0\'-\'9\').'] } }) });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Add staff' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: 'Asha' } });
    fireEvent.change(within(dialog).getByLabelText('Email'), { target: { value: 'asha@test.local' } });
    fireEvent.change(within(dialog).getByLabelText('Password'), { target: { value: 'Password#' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

    expect(await screen.findByText(/at least one digit/)).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});
