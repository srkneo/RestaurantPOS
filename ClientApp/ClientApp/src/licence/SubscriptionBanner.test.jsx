// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import SubscriptionBanner from './SubscriptionBanner';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const mock = (sub) => vi.stubGlobal('fetch', vi.fn(async (url) => (url === '/api/subscription'
  ? { ok: true, status: 200, json: async () => sub }
  : { ok: false, status: 404, json: async () => null })));

describe('Subscription banner', () => {
  it('warns in the last week of the trial', async () => {
    mock({ plan: 'trial', paidUntil: '2026-12-01', state: 'trial', daysLeft: 3, graceDays: 7, monthly: 1000, yearly: 10000 });
    render(<I18nProvider><SubscriptionBanner /></I18nProvider>);
    expect(await screen.findByText(/Free trial ends in 3 days/)).toBeInTheDocument();
  });

  it('says new orders are stopped when the subscription has ended', async () => {
    mock({ plan: 'monthly', paidUntil: '2026-11-01', state: 'suspended', daysLeft: -18, graceDays: 7, monthly: 1000, yearly: 10000 });
    render(<I18nProvider><SubscriptionBanner /></I18nProvider>);
    expect(await screen.findByRole('alert')).toHaveTextContent('new orders and bills are stopped');
  });

  it('shows nothing early in the trial or for free use', async () => {
    mock({ plan: 'trial', paidUntil: '2026-12-19', state: 'trial', daysLeft: 25, graceDays: 7, monthly: 1000, yearly: 10000 });
    const { container } = render(<I18nProvider><SubscriptionBanner /></I18nProvider>);
    await new Promise((r) => setTimeout(r, 20));
    expect(container).toBeEmptyDOMElement();
  });
});
