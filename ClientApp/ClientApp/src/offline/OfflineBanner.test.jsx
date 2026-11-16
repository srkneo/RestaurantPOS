// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import OfflineBanner from './OfflineBanner';
import { saveOffline, listOffline, resetOfflineForTests } from './orderQueue';

const order = (id, name) => ({ clientOrderId: id, orderType: 'Takeaway', customerName: name, items: [{ menuItemId: 1, quantity: 1 }] });
const renderBanner = () => render(<I18nProvider><ToastProvider><OfflineBanner /></ToastProvider></I18nProvider>);

beforeEach(() => resetOfflineForTests());
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Offline banner', () => {
  it('shows nothing when online with no waiting orders', async () => {
    vi.stubGlobal('fetch', vi.fn());
    renderBanner();
    await new Promise((r) => setTimeout(r, 20));
    // (the toast area of ToastProvider is always on the page, so check the banner's own texts)
    expect(screen.queryByText('No connection to the server.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Send now' })).not.toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
  });

  it('shows waiting orders while the server is down, then sends them', async () => {
    await saveOffline(order('a', 'Asha'));
    let up = false;
    vi.stubGlobal('fetch', vi.fn(async () => {
      if (!up) throw new TypeError('Failed to fetch');
      return { ok: true, status: 200, json: async () => ({ orderId: 9, tokenNumber: 12 }) };
    }));
    renderBanner();
    expect(await screen.findByText('1 order(s) waiting to be sent.')).toBeInTheDocument();

    up = true;
    fireEvent.click(screen.getByRole('button', { name: 'Send now' }));
    expect(await screen.findByText('Offline order sent. Token 12')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('1 order(s) waiting to be sent.')).not.toBeInTheDocument());
    expect(await listOffline()).toHaveLength(0);
  });

  it('shows a refused order with its reason and lets the cashier remove it', async () => {
    await saveOffline(order('bad', 'Ravi'));
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: false, status: 400, json: async () => ({ message: 'An item in the order is not on the menu.' }) })));
    renderBanner();
    expect(await screen.findByText(/An item in the order is not on the menu\./)).toBeInTheDocument();
    expect(screen.getByText(/Ravi/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    await waitFor(async () => expect(await listOffline()).toHaveLength(0));
  });
});
