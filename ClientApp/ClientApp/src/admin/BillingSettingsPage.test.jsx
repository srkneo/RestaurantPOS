// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import BillingSettingsPage from './BillingSettingsPage';

const CURRENT = { taxMode: 'None', pricesIncludeTax: false, billPrefix: 'INV', upiId: null, upiName: null, paperWidth: 80, printLanguage: 'en' };

function mockApi({ get = { ok: true, status: 200, json: async () => CURRENT }, put } = {}) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (options?.method === 'PUT') return put ?? { ok: true, status: 200, json: async () => JSON.parse(options.body) };
    return get;
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><BillingSettingsPage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Billing settings page', () => {
  it('shows the current settings', async () => {
    const calls = mockApi({ get: { ok: true, status: 200, json: async () => ({ ...CURRENT, upiId: 'shop@okaxis', paperWidth: 58, printLanguage: 'gu' }) } });
    renderPage();
    expect(await screen.findByRole('heading', { name: 'Billing settings' })).toBeInTheDocument();
    expect(calls[0].url).toBe('/api/admin/billing-settings');
    expect(screen.getByLabelText('GST')).toHaveValue('None');
    expect(screen.getByLabelText('Bill number prefix')).toHaveValue('INV');
    expect(screen.getByLabelText('UPI ID for the payment QR')).toHaveValue('shop@okaxis');
    expect(screen.getByLabelText('Receipt paper')).toHaveValue('58');
    expect(screen.getByLabelText('Bill and KOT language')).toHaveValue('gu');
    expect(screen.queryByText('GST bills need your GSTIN. Add it on the Store page.')).not.toBeInTheDocument();
  });

  it('saves the changes, tidied', async () => {
    const calls = mockApi();
    renderPage();
    fireEvent.change(await screen.findByLabelText('GST'), { target: { value: 'Regular' } });
    expect(screen.getByText('GST bills need your GSTIN. Add it on the Store page.')).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Menu prices already include GST'));
    fireEvent.change(screen.getByLabelText('Bill number prefix'), { target: { value: ' an1 ' } });
    fireEvent.change(screen.getByLabelText('UPI ID for the payment QR'), { target: { value: ' shop@okaxis ' } });
    fireEvent.change(screen.getByLabelText('Name shown in the UPI app'), { target: { value: '' } });
    fireEvent.change(screen.getByLabelText('Receipt paper'), { target: { value: '58' } });
    fireEvent.change(screen.getByLabelText('Bill and KOT language'), { target: { value: 'gu' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Billing settings saved.')).toBeInTheDocument();
    const put = calls.find((c) => c.options?.method === 'PUT');
    expect(put.url).toBe('/api/admin/billing-settings');
    expect(JSON.parse(put.options.body)).toEqual({
      taxMode: 'Regular', pricesIncludeTax: true, billPrefix: 'AN1', upiId: 'shop@okaxis', upiName: null, paperWidth: 58, printLanguage: 'gu',
    });
  });

  it('shows the server errors next to the fields', async () => {
    mockApi({ put: { ok: false, status: 400, json: async () => ({ errors: { BillPrefix: ['Use 1 to 3 letters or digits, for example INV.'], UpiId: ['Enter a UPI ID like yourname@okaxis.'] } }) } });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Use 1 to 3 letters or digits, for example INV.')).toBeInTheDocument();
    expect(screen.getByText('Enter a UPI ID like yourname@okaxis.')).toBeInTheDocument();
  });

  it('offers a retry when loading fails', async () => {
    mockApi({ get: { ok: false, status: 500, json: async () => ({}) } });
    renderPage();
    expect(await screen.findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
