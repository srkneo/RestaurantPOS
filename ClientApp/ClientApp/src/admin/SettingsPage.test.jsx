// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import SettingsPage from './SettingsPage';

const SETTINGS = {
  timeZone: 'Asia/Kolkata', businessDayStartHour: 4, dineInEnabled: true, takeawayEnabled: true, defaultOrderType: 'DineIn',
  takeawayNeedsName: true, maxItemQuantity: 99, firstToken: 1, kitchenScreenEnabled: true,
};
const COUNTER = { ...SETTINGS, dineInEnabled: false, defaultOrderType: 'Takeaway', takeawayNeedsName: false };

function mockApi(save = { ok: true, status: 200, json: async () => SETTINGS }) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/admin/settings/preset') return { ok: true, status: 200, json: async () => COUNTER };
    if (options?.method === 'PUT') return save;
    return { ok: true, json: async () => SETTINGS };
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><SettingsPage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Settings page', () => {
  it('loads the settings', async () => {
    mockApi();
    renderPage();
    expect(await screen.findByLabelText('Dine-in orders')).toBeChecked();
    expect(screen.getByLabelText('Time zone')).toHaveValue('Asia/Kolkata');
    expect(screen.getByLabelText('First token of the day')).toHaveValue(1);
    expect(screen.getByLabelText('POS starts with')).toHaveValue('DineIn');
  });

  it('applies a quick setup', async () => {
    const calls = mockApi();
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Takeaway counter' }));
    expect(await screen.findByText('Setup applied.')).toBeInTheDocument();
    expect(JSON.parse(calls.find((c) => c.url === '/api/admin/settings/preset').options.body)).toEqual({ preset: 'takeaway-counter' });
    expect(screen.getByLabelText('Dine-in orders')).not.toBeChecked();
    expect(screen.getByLabelText('POS starts with')).toHaveValue('Takeaway');
  });

  it('saves with numbers as numbers', async () => {
    const calls = mockApi();
    renderPage();
    fireEvent.change(await screen.findByLabelText('First token of the day'), { target: { value: '101' } });
    fireEvent.click(screen.getByLabelText('Ask the customer\'s name for takeaway'));
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    expect(await screen.findByText('Settings saved.')).toBeInTheDocument();
    const put = calls.find((c) => c.options?.method === 'PUT');
    expect(put.url).toBe('/api/admin/settings');
    expect(JSON.parse(put.options.body)).toEqual({ ...SETTINGS, firstToken: 101, takeawayNeedsName: false });
  });

  it('shows the server rule that was broken', async () => {
    mockApi({ ok: false, status: 400, json: async () => ({ errors: { ordertypes: ['Turn on at least one order type.'] } }) });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Save settings' }));
    expect(await screen.findByText('Turn on at least one order type.')).toBeInTheDocument();
  });
});
