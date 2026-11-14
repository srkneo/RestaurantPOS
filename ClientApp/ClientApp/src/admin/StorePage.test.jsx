// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import StorePage from './StorePage';

const PROFILE = {
  restaurantName: 'My Restaurant', outletName: 'Main', address: null, phone: null, gstin: null, fssai: null, receiptFooter: null, hasLogo: false,
};

function mockApi({ save = { ok: true, status: 200, json: async () => ({ ...PROFILE, restaurantName: 'Shree Krishna Dining' }) } } = {}) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/admin/store' && options?.method === 'PUT') return save;
    if (options?.method) return { ok: true, status: 204, json: async () => ({}) };
    return { ok: true, json: async () => PROFILE };
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><StorePage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Store profile page', () => {
  it('loads the profile and saves changes', async () => {
    const calls = mockApi();
    renderPage();
    expect(await screen.findByDisplayValue('My Restaurant')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Restaurant name'), { target: { value: 'Shree Krishna Dining' } });
    fireEvent.change(screen.getByLabelText('GSTIN'), { target: { value: '24AAACC1206D1ZM' } });
    fireEvent.change(screen.getByLabelText('Bill footer'), { target: { value: 'Thank you, visit again' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Store profile saved.')).toBeInTheDocument();
    const put = calls.find((c) => c.options?.method === 'PUT');
    expect(JSON.parse(put.options.body)).toEqual({
      restaurantName: 'Shree Krishna Dining', outletName: 'Main', address: '', phone: '', gstin: '24AAACC1206D1ZM', fssai: '', receiptFooter: 'Thank you, visit again',
    });
  });

  it('needs the names before saving', async () => {
    const calls = mockApi();
    renderPage();
    fireEvent.change(await screen.findByLabelText('Outlet name'), { target: { value: ' ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(screen.getByText('Enter a name.')).toBeInTheDocument();
    expect(calls.some((c) => c.options?.method === 'PUT')).toBe(false);
  });

  it('shows a wrong GSTIN next to the field', async () => {
    mockApi({ save: { ok: false, status: 400, json: async () => ({ errors: { gstin: ['Enter a valid 15-character GSTIN, e.g. 24AAACC1206D1ZM.'] } }) } });
    renderPage();
    await screen.findByDisplayValue('My Restaurant');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Enter a valid 15-character GSTIN, e.g. 24AAACC1206D1ZM.')).toBeInTheDocument();
  });

  it('uploads and removes the logo', async () => {
    const calls = mockApi();
    renderPage();
    await screen.findByDisplayValue('My Restaurant');
    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'logo.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload logo'), { target: { files: [file] } });

    expect(await screen.findByText('Logo saved.')).toBeInTheDocument();
    const upload = calls.find((c) => c.url === '/api/admin/store/logo' && c.options?.method === 'POST');
    expect(upload.options.body).toBeInstanceOf(FormData);
    expect(upload.options.body.get('file')).toBe(file);
    expect(screen.getByRole('img', { name: 'Logo' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Remove logo' }));
    expect(await screen.findByText('Logo removed.')).toBeInTheDocument();
    expect(calls.some((c) => c.url === '/api/admin/store/logo' && c.options?.method === 'DELETE')).toBe(true);
    expect(screen.queryByRole('img', { name: 'Logo' })).not.toBeInTheDocument();
  });
});
