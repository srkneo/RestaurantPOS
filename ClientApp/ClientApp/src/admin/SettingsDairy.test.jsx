// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import SettingsPage from './SettingsPage';

const DAIRY = {
  timeZone: 'Asia/Kolkata', businessDayStartHour: 4, dineInEnabled: false, takeawayEnabled: true, defaultOrderType: 'Takeaway',
  takeawayNeedsName: false, maxItemQuantity: 99, firstToken: 1, kitchenScreenEnabled: true, simpleCounter: true,
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Dairy shop quick setup', () => {
  it('shows the dairy shop as the setup in use', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => DAIRY })));
    render(<I18nProvider><ToastProvider><SettingsPage /></ToastProvider></I18nProvider>);
    expect(await screen.findByText('Current setup: Dairy shop')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Dairy shop' })).toHaveAttribute('aria-pressed', 'true');
  });
});
