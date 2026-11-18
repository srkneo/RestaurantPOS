// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import SetupPage from './SetupPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  localStorage.clear();
});

const ANSWERS = {
  '/api/admin/store': { restaurantName: 'Anand Thali', outletName: 'Main', phone: '9429549007', gstin: null },
  '/api/admin/menu/items': [{ id: 1, name: 'Tea' }],
  '/api/admin/tables': [{ id: 1, tableNumber: '1' }],
  '/api/staff': [{ id: 'a', email: 'owner@test.local' }],
  '/api/admin/billing-settings': { taxMode: 'None', upiId: null },
  '/api/admin/tables/qr-setting': { enabled: false },
};

describe('Setup checklist', () => {
  it('ticks what is done from the data and lets the owner tick the printer test', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url) => ({ ok: true, status: 200, json: async () => ANSWERS[url] })));
    render(<MemoryRouter><I18nProvider><SetupPage /></I18nProvider></MemoryRouter>);

    expect(await screen.findByText('3 steps left before the first day of billing.')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: 'Store details' })).getByText('Done')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: 'Billing and UPI' })).getByText('To do')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: 'Staff' })).getByText('To do')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'I printed a test bill' }));
    expect(screen.getByText('2 steps left before the first day of billing.')).toBeInTheDocument();
    expect(localStorage.getItem('pos.setup.printerTested')).toBe('yes');
  });
});
