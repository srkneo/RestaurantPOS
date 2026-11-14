// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, act, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import KitchenDisplay from './KitchenDisplay';
import { handlers } from '../test/signalr-mock';

vi.mock('@microsoft/signalr', () => import('../test/signalr-mock'));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

// The live "status changed" message can arrive before the reply to our own request.
// The screen must still end up with the server's version, not one too high.
describe('Kitchen screen version after a live update', () => {
  it('sends the right version on Mark ready when the live message arrives first', async () => {
    let finishPut;
    const puts = [];
    vi.stubGlobal('fetch', vi.fn((url, options) => {
      if (options?.method === 'PUT') {
        puts.push(JSON.parse(options.body));
        if (puts.length === 1) return new Promise((resolve) => { finishPut = () => resolve({ ok: true, status: 200, json: async () => ({}) }); });
        return Promise.resolve({ ok: true, status: 200, json: async () => ({}) });
      }
      return Promise.resolve({ ok: true, json: async () => [
        { id: 5, tokenNumber: 2, tableId: 1, status: 'Received', version: 1, createdAt: new Date().toISOString(), items: [] },
      ] });
    }));
    render(<MemoryRouter><I18nProvider><ToastProvider><KitchenDisplay /></ToastProvider></I18nProvider></MemoryRouter>);
    const ticket = await screen.findByRole('article', { name: 'Token 2' });

    fireEvent.click(within(ticket).getByRole('button', { name: 'Start cooking' }));
    act(() => { handlers.OrderStatusChanged({ orderId: 5, tokenNumber: 2, status: 'Preparing', version: 2 }); });
    await act(async () => { finishPut(); });

    fireEvent.click(within(ticket).getByRole('button', { name: 'Mark ready' }));
    await vi.waitFor(() => expect(puts).toHaveLength(2));
    expect(puts[0]).toEqual({ status: 'Preparing', version: 1 });
    expect(puts[1]).toEqual({ status: 'Ready', version: 2 });
  });
});
