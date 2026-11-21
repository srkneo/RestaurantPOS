// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import CloudMenuBanner from './CloudMenuBanner';

const renderBanner = (onSynced) => render(<I18nProvider><ToastProvider><CloudMenuBanner onSynced={onSynced} /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Cloud menu banner', () => {
  it('says the menu comes from the cloud and updates it on request', async () => {
    const calls = [];
    const status = { managedByCloud: true, version: 'v1', syncedAt: '2026-11-21T08:00:00Z', error: null };
    vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
      calls.push({ url, method: options.method || 'GET' });
      return { ok: true, status: 200, json: async () => status };
    }));
    const onSynced = vi.fn();
    renderBanner(onSynced);

    expect(await screen.findByText(/managed in the cloud/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Update now' }));
    await waitFor(() => expect(onSynced).toHaveBeenCalled());
    expect(calls.filter((c) => c.method === 'POST').map((c) => c.url)).toEqual(['/api/admin/menu/sync/now']);
  });

  it('shows nothing when the shop keeps its own menu', async () => {
    const fetchMock = vi.fn(async () => ({ ok: true, status: 200, json: async () => ({ managedByCloud: false }) }));
    vi.stubGlobal('fetch', fetchMock);
    renderBanner();
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: 'Update now' })).not.toBeInTheDocument();
  });
});
