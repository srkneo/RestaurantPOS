// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import QrSheetPage from './QrSheetPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('QR sheet', () => {
  it('shows a QR code per table and turns ordering by QR on', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
      calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
      if (url === '/api/admin/tables/qr') return { ok: true, status: 200, json: async () => [{ id: 3, tableNumber: '5', token: 'tok5' }] };
      return { ok: true, status: 200, json: async () => ({ enabled: false }) };
    }));
    render(<MemoryRouter><I18nProvider><ToastProvider><QrSheetPage /></ToastProvider></I18nProvider></MemoryRouter>);

    const img = await screen.findByRole('img', { name: 'Table 5' });
    expect(img.getAttribute('src')).toContain('/api/admin/tables/3/qr.svg?origin=');
    fireEvent.click(screen.getByRole('switch', { name: 'Guests can order from the QR code' }));
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ enabled: true });
  });
});
