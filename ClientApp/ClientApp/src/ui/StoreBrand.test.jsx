// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import StoreBrand from './StoreBrand';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Store name in the header', () => {
  it('shows the restaurant name and logo', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, json: async () => ({ restaurantName: 'Shree Krishna Dining', outletName: 'Anand', hasLogo: true }) })));
    const { container } = render(<I18nProvider><StoreBrand /></I18nProvider>);
    expect(await screen.findByText('Shree Krishna Dining')).toBeInTheDocument();
    expect(container.querySelector('img')).toHaveAttribute('src', '/api/store/logo');
  });

  it('falls back to the app name', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: false, json: async () => ({}) })));
    const { container } = render(<I18nProvider><StoreBrand /></I18nProvider>);
    expect(await screen.findByText('Restaurant POS')).toBeInTheDocument();
    expect(container.querySelector('img')).toBeNull();
  });
});
