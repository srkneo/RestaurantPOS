// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import SettingsPage from './SettingsPage';

const CAFE = {
  timeZone: 'Asia/Kolkata', businessDayStartHour: 4, dineInEnabled: true, takeawayEnabled: true, defaultOrderType: 'Takeaway',
  takeawayNeedsName: true, maxItemQuantity: 99, firstToken: 1, kitchenScreenEnabled: true,
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const show = (settings) => {
  vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => settings })));
  render(<I18nProvider><ToastProvider><SettingsPage /></ToastProvider></I18nProvider>);
};

describe('Settings quick setup', () => {
  it('marks the outlet type in use with aria-pressed', async () => {
    show(CAFE);
    expect(await screen.findByText('Current setup: Cafe')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cafe' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Takeaway counter' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('says custom when settings were changed by hand', async () => {
    show({ ...CAFE, kitchenScreenEnabled: false });
    expect(await screen.findByText('Current setup: custom (changed by hand)')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cafe' })).toHaveAttribute('aria-pressed', 'false');
    fireEvent.click(screen.getByRole('button', { name: 'Cafe' }));
  });
});
