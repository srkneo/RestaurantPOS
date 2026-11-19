// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import StockPage from './StockPage';

const auth = vi.hoisted(() => ({ roles: ['manager'] }));
vi.mock('../AuthContext', () => ({ useAuth: () => ({ user: { roles: auth.roles } }) }));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  auth.roles = ['manager'];
});

const milk = { id: 1, name: 'Milk', unit: 'l', quantity: 10, lowLevel: 5, costPerUnit: 55, isLow: false };
const sugar = { id: 2, name: 'Sugar', unit: 'kg', quantity: 1, lowLevel: 2, costPerUnit: null, isLow: true };

const mock = (handler) => {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    const call = { url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null };
    calls.push(call);
    const [status, body] = handler(call);
    return { ok: status < 300, status, json: async () => body };
  }));
  return calls;
};

const show = () => render(<I18nProvider><ToastProvider><StockPage /></ToastProvider></I18nProvider>);

describe('Stock page', () => {
  it('lists stock, marks low items and filters to low only', async () => {
    mock(() => [200, [milk, sugar]]);
    show();
    expect(await screen.findByText('10 l')).toBeInTheDocument();
    expect(screen.getByText('1 item(s) low on stock')).toBeInTheDocument();
    expect(screen.getByText('Low')).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Only low stock'));
    expect(screen.queryByText('10 l')).not.toBeInTheDocument();
    expect(screen.getByText('1 kg')).toBeInTheDocument();
  });

  it('records a purchase with its cost', async () => {
    let list = [milk];
    const calls = mock((c) => {
      if (c.method === 'POST') {
        list = [{ ...milk, quantity: 30 }];
        return [200, list[0]];
      }
      return [200, list];
    });
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Record for Milk' }));
    fireEvent.change(screen.getByLabelText('Quantity (l)'), { target: { value: '20' } });
    fireEvent.change(screen.getByLabelText('Total paid (optional)'), { target: { value: '1100' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Milk: 30 l in stock.')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST')).toMatchObject({ url: '/api/stock/1/moves', body: { kind: 'Purchase', quantity: 20, cost: 1100, note: null } });
  });

  it('shows the server error next to the field', async () => {
    mock((c) => (c.method === 'POST'
      ? [400, { errors: { quantity: ['Only 10 l in stock.'] } }]
      : [200, [milk]]));
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Record for Milk' }));
    fireEvent.change(screen.getByLabelText('What happened'), { target: { value: 'Used' } });
    expect(screen.queryByLabelText('Total paid (optional)')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Quantity (l)'), { target: { value: '12' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Only 10 l in stock.')).toBeInTheDocument();
  });

  it('lets a manager add an item but not a cashier', async () => {
    const calls = mock((c) => (c.method === 'POST' ? [201, { ...sugar, id: 3, name: 'Tea', quantity: 2, isLow: false }] : [200, []]));
    show();
    expect(await screen.findByText('No stock items yet.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add stock item' }));
    fireEvent.change(screen.getByLabelText('Item'), { target: { value: 'Tea' } });
    fireEvent.change(screen.getByLabelText('Quantity in stock now'), { target: { value: '2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Tea: 2 kg in stock.')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST').body).toEqual({ name: 'Tea', unit: 'kg', lowLevel: 0, quantity: 2 });

    cleanup();
    auth.roles = ['cashier'];
    mock(() => [200, [milk]]);
    show();
    expect(await screen.findByRole('button', { name: 'Record for Milk' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add stock item' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit Milk' })).not.toBeInTheDocument();
  });
});
