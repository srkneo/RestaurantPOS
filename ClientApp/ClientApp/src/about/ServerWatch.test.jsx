// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import ServerWatch from './ServerWatch';
import { clearAbout } from './about';

function mockServer(state) {
  vi.stubGlobal('fetch', vi.fn(async (url) => {
    if (url === '/health') {
      if (!state.up) throw new TypeError('Failed to fetch');
      return { ok: true, status: 200 };
    }
    if (url === '/api/about') {
      if (!state.up) throw new TypeError('Failed to fetch');
      return { ok: true, status: 200, json: async () => ({ companyName: 'Vyom Solutions', productName: 'RestaurantPOS', version: state.version, shopPc: state.shopPc }) };
    }
    return { ok: false, status: 404, json: async () => ({}) };
  }));
}

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const renderWatch = (props) => render(<I18nProvider><ServerWatch checkEvery={20} downAfter={2} versionEvery={100000} {...props} /></I18nProvider>);

afterEach(() => {
  cleanup();
  clearAbout();
  vi.unstubAllGlobals();
});

describe('Server watch', () => {
  it('covers a shop PC screen while the server restarts, then offers the new version', async () => {
    const state = { up: true, version: '1.7.6', shopPc: true };
    mockServer(state);
    renderWatch();
    await wait(80);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    state.up = false;
    expect(await screen.findByRole('alertdialog')).toHaveTextContent('The POS is restarting');

    state.version = '1.7.7';
    state.up = true;
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(await screen.findByText('Version v1.7.7 is ready. Reload the page when the current order is done.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
  });

  it('never covers the screen on the cloud, where the offline order queue takes over', async () => {
    const state = { up: true, version: '1.7.6', shopPc: false };
    mockServer(state);
    renderWatch();
    await wait(40);
    state.up = false;
    await wait(150);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('shows nothing while all is well', async () => {
    mockServer({ up: true, version: '1.7.6', shopPc: true });
    renderWatch();
    await wait(100);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reload' })).not.toBeInTheDocument();
  });
});
