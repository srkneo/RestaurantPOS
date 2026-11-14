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

const minutesAgo = (m) => new Date(Date.now() - m * 60000).toISOString();

function mockApi(orders, putOk = true) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (options?.method === 'PUT') return { ok: putOk, json: async () => ({}) };
    if (url === '/api/orders') return { ok: true, json: async () => orders };
    return { ok: false, json: async () => ({}) };
  }));
  return calls;
}

function renderKitchen() {
  render(<MemoryRouter><I18nProvider><ToastProvider><KitchenDisplay /></ToastProvider></I18nProvider></MemoryRouter>);
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Kitchen screen', () => {
  it('shows waiting tickets with new, waiting and late labels', async () => {
    mockApi([
      { id: 1, tokenNumber: 101, tableId: 1, status: 'Received', createdAt: minutesAgo(14), items: [{ id: 1, quantity: 2, itemName: 'Classic Burger' }] },
      { id: 2, tokenNumber: 102, tableId: 2, status: 'Received', createdAt: minutesAgo(7), items: [] },
      { id: 3, tokenNumber: 103, tableId: 4, status: 'Received', createdAt: minutesAgo(1), items: [] },
      { id: 4, tokenNumber: 99, tableId: 3, status: 'Ready', createdAt: minutesAgo(30), items: [] },
    ]);
    renderKitchen();

    const late = await screen.findByRole('article', { name: 'Token 101' });
    expect(within(late).getByText('Late, 14 min')).toBeInTheDocument();
    expect(within(late).getByText('2 x Classic Burger')).toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: 'Token 102' })).getByText('Waiting, 7 min')).toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: 'Token 103' })).getByText('New, 1 min')).toBeInTheDocument();
    expect(screen.queryByRole('article', { name: 'Token 99' })).not.toBeInTheDocument();
    expect(screen.getByText('3 orders waiting')).toBeInTheDocument();
  });

  it('removes a ticket when it is marked ready', async () => {
    const calls = mockApi([{ id: 5, tokenNumber: 12, tableId: 1, status: 'Received', createdAt: minutesAgo(2), items: [] }]);
    renderKitchen();
    const ticket = await screen.findByRole('article', { name: 'Token 12' });

    fireEvent.click(within(ticket).getByRole('button', { name: 'Mark ready' }));

    await vi.waitFor(() => expect(screen.queryByRole('article', { name: 'Token 12' })).not.toBeInTheDocument());
    const put = calls.find((c) => c.options?.method === 'PUT');
    expect(put.url).toBe('/api/orders/5/status');
    expect(JSON.parse(put.options.body)).toEqual({ status: 'Ready' });
  });

  it('keeps the ticket and shows an error when marking fails', async () => {
    mockApi([{ id: 6, tokenNumber: 13, tableId: 1, status: 'Received', createdAt: minutesAgo(2), items: [] }], false);
    renderKitchen();
    const ticket = await screen.findByRole('article', { name: 'Token 13' });

    fireEvent.click(within(ticket).getByRole('button', { name: 'Mark ready' }));

    expect(await screen.findByText('Could not mark token 13 ready. Try again.')).toBeInTheDocument();
    expect(screen.getByRole('article', { name: 'Token 13' })).toBeInTheDocument();
  });

  it('adds new orders that arrive live', async () => {
    mockApi([]);
    renderKitchen();
    expect(await screen.findByText('No orders waiting.')).toBeInTheDocument();

    act(() => {
      handlers.ReceiveNewOrder({ id: 7, tokenNumber: 14, tableId: 2, status: 'Received', createdAt: minutesAgo(0), items: [{ id: 9, quantity: 1, itemName: 'Fries' }] });
    });

    expect(screen.getByRole('article', { name: 'Token 14' })).toBeInTheDocument();
    expect(screen.getByText('1 x Fries')).toBeInTheDocument();
  });
});
