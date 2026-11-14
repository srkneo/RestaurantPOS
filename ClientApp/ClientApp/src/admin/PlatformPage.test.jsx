// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import PlatformPage from './PlatformPage';

const TENANTS = [
  { id: 1, name: 'My Restaurant', isActive: true, createdAt: '2026-11-14T00:00:00Z', outletCount: 1, staffCount: 4, lastOrderAt: '2026-11-14T10:30:00Z' },
  { id: 2, name: 'Anand Thali House', isActive: false, createdAt: '2026-11-14T00:00:00Z', outletCount: 1, staffCount: 1, lastOrderAt: null },
];

function mockApi(write = { ok: true, status: 200, json: async () => ({}) }, list = TENANTS) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (options?.method) return write;
    return { ok: true, json: async () => list };
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><PlatformPage /></ToastProvider></I18nProvider>);

const fill = (dialog, label, value) => fireEvent.change(within(dialog).getByLabelText(label), { target: { value } });

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Platform page', () => {
  it('lists restaurants with status, staff and last order', async () => {
    const calls = mockApi();
    renderPage();
    expect(await screen.findByRole('heading', { name: 'Restaurants' })).toBeInTheDocument();
    expect(calls[0].url).toBe('/api/platform/tenants');

    const active = (await screen.findByText('My Restaurant')).closest('tr');
    expect(within(active).getByText('Active')).toBeInTheDocument();
    expect(within(active).getByText('4')).toBeInTheDocument();
    expect(within(active).getByRole('button', { name: 'Disable' })).toBeInTheDocument();

    const disabled = screen.getByText('Anand Thali House').closest('tr');
    expect(within(disabled).getByText('Disabled')).toBeInTheDocument();
    expect(within(disabled).getByText('No orders yet')).toBeInTheDocument();
    expect(within(disabled).getByRole('button', { name: 'Enable' })).toBeInTheDocument();

    expect(screen.queryByText('No restaurants yet.')).not.toBeInTheDocument();
  });

  it('shows a message when there are no restaurants', async () => {
    mockApi(undefined, []);
    renderPage();
    expect(await screen.findByText('No restaurants yet.')).toBeInTheDocument();
  });

  it('adds a restaurant after checking the fields', async () => {
    const calls = mockApi({ ok: true, status: 201, json: async () => ({}) });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Add restaurant' }));
    const dialog = screen.getByRole('dialog');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Create' }));
    expect(within(dialog).getAllByText('Fill in this field.')).toHaveLength(5);
    expect(calls.some((c) => c.options?.method === 'POST')).toBe(false);

    fill(dialog, 'Restaurant name', ' Anand Thali House ');
    fill(dialog, 'First outlet name', 'Station Road');
    fill(dialog, "Owner's name", 'Ravi Patel');
    fill(dialog, "Owner's email (used to sign in)", ' ravi@thali.local ');
    fill(dialog, "Owner's first password", 'Owner#2026xy');
    expect(within(dialog).getByLabelText("Owner's first password")).toHaveAttribute('type', 'password');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create' }));

    expect(await screen.findByText('Restaurant added. The owner can sign in now.')).toBeInTheDocument();
    const post = calls.find((c) => c.options?.method === 'POST');
    expect(post.url).toBe('/api/platform/tenants');
    expect(JSON.parse(post.options.body)).toEqual({
      restaurantName: 'Anand Thali House',
      outletName: 'Station Road',
      ownerName: 'Ravi Patel',
      ownerEmail: 'ravi@thali.local',
      ownerPassword: 'Owner#2026xy',
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows the server error next to the field', async () => {
    mockApi({ ok: false, status: 400, json: async () => ({ errors: { owneremail: ['This email is already used by another account.'] } }) });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Add restaurant' }));
    const dialog = screen.getByRole('dialog');
    fill(dialog, 'Restaurant name', 'A');
    fill(dialog, 'First outlet name', 'B');
    fill(dialog, "Owner's name", 'C');
    fill(dialog, "Owner's email (used to sign in)", 'used@thali.local');
    fill(dialog, "Owner's first password", 'Owner#2026xy');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create' }));
    expect(await within(dialog).findByText('This email is already used by another account.')).toBeInTheDocument();
  });

  it('asks before disabling a restaurant', async () => {
    const calls = mockApi();
    renderPage();
    const row = (await screen.findByText('My Restaurant')).closest('tr');
    fireEvent.click(within(row).getByRole('button', { name: 'Disable' }));

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Disable My Restaurant?')).toBeInTheDocument();
    expect(calls.some((c) => c.options?.method === 'POST')).toBe(false);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disable' }));

    expect(await screen.findByText('My Restaurant is disabled.')).toBeInTheDocument();
    const post = calls.find((c) => c.options?.method === 'POST');
    expect(post.url).toBe('/api/platform/tenants/1/disable');
  });

  it('enables a disabled restaurant at once', async () => {
    const calls = mockApi();
    renderPage();
    const row = (await screen.findByText('Anand Thali House')).closest('tr');
    fireEvent.click(within(row).getByRole('button', { name: 'Enable' }));

    expect(await screen.findByText('Anand Thali House is enabled.')).toBeInTheDocument();
    const post = calls.find((c) => c.options?.method === 'POST');
    expect(post.url).toBe('/api/platform/tenants/2/enable');
  });
});
