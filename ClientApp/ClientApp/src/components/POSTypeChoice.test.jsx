// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, act } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import POSMenu from './POSMenu';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

const DINE_IN = {
  timeZone: 'Asia/Kolkata', businessDayStartHour: 4, dineInEnabled: true, takeawayEnabled: true, defaultOrderType: 'DineIn',
  takeawayNeedsName: true, maxItemQuantity: 99, firstToken: 1, kitchenScreenEnabled: true,
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('POS order type', () => {
  it('keeps the cashier choice when the outlet settings arrive later', async () => {
    let sendSettings;
    const settingsArrive = new Promise((resolve) => { sendSettings = resolve; });
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      if (url === '/api/menuitems') return { ok: true, json: async () => [{ id: 3, name: 'Fries', price: 3, category: 'Sides', isVeg: true }] };
      if (url === '/api/tables') return { ok: true, json: async () => [{ id: 1, tableNumber: '1' }] };
      if (url === '/api/store/settings') { await settingsArrive; return { ok: true, json: async () => DINE_IN }; }
      return { ok: false, status: 404, json: async () => ({}) };
    }));
    render(<MemoryRouter initialEntries={['/pos']}><I18nProvider><ToastProvider><POSMenu /></ToastProvider></I18nProvider></MemoryRouter>);

    fireEvent.click(await screen.findByRole('button', { name: 'Takeaway' }));
    await act(async () => { sendSettings(); });

    expect(await screen.findByRole('button', { name: 'Add Fries' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Takeaway' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByLabelText('Customer name')).toBeInTheDocument();
  });
});
