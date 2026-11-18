// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, act, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import TokenDisplay from './TokenDisplay';
import { handlers } from '../test/signalr-mock';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Token display', () => {
  it('shows tokens being prepared and ready, and moves them live', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, json: async () => [
      { id: 1, tokenNumber: 12, status: 'Preparing' },
      { id: 2, tokenNumber: 9, status: 'Ready' },
      { id: 3, tokenNumber: 5, status: 'Completed' },
    ] })));
    render(<I18nProvider><TokenDisplay /></I18nProvider>);

    const preparing = screen.getByRole('region', { name: 'Preparing' });
    const ready = screen.getByRole('region', { name: 'Ready to collect' });
    expect(await within(preparing).findByText('12')).toBeInTheDocument();
    expect(within(ready).getByText('9')).toBeInTheDocument();
    expect(screen.queryByText('5')).not.toBeInTheDocument();

    act(() => { handlers.OrderStatusChanged({ orderId: 1, tokenNumber: 12, status: 'Ready', version: 3 }); });
    expect(within(ready).getByText('12')).toBeInTheDocument();
    act(() => { handlers.OrderStatusChanged({ orderId: 2, tokenNumber: 9, status: 'Completed', version: 4 }); });
    expect(screen.queryByText('9')).not.toBeInTheDocument();
    act(() => { handlers.ReceiveNewOrder({ id: 4, tokenNumber: 13, status: 'Received' }); });
    expect(within(preparing).getByText('13')).toBeInTheDocument();
  });
});
