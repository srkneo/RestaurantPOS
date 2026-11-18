// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import ExpensesPage from './ExpensesPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Expenses page', () => {
  it('records an expense paid from the till and lists today', async () => {
    const calls = [];
    let list = { businessDate: '2026-11-18', total: 0, fromTill: 0, expenses: [] };
    vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
      calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
      if (options.method === 'POST') {
        list = { ...list, total: 120, fromTill: 120,
          expenses: [{ id: 1, businessDate: '2026-11-18', category: 'Milk', amount: 120, paidFrom: 'Till', note: 'Amul', at: '2026-11-18T05:00:00Z' }] };
        return { ok: true, status: 201, json: async () => list.expenses[0] };
      }
      return { ok: true, status: 200, json: async () => list };
    }));
    render(<I18nProvider><ToastProvider><ExpensesPage /></ToastProvider></I18nProvider>);

    expect(await screen.findByText('No expenses recorded today.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'Milk' } });
    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '120' } });
    fireEvent.change(screen.getByLabelText('Note'), { target: { value: 'Amul' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save expense' }));

    expect(await screen.findByText('Expense of ₹120.00 saved.')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST').body).toEqual({ category: 'Milk', amount: 120, paidFrom: 'Till', note: 'Amul' });
    expect(await screen.findByText('Amul')).toBeInTheDocument();
  });

  it('checks the amount and shows server errors', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => (options.method === 'POST'
      ? { ok: false, status: 400, json: async () => ({ message: 'Open a till shift first, or choose paid from elsewhere.' }) }
      : { ok: true, status: 200, json: async () => ({ businessDate: '2026-11-18', total: 0, fromTill: 0, expenses: [] }) })));
    render(<I18nProvider><ToastProvider><ExpensesPage /></ToastProvider></I18nProvider>);
    fireEvent.click(await screen.findByRole('button', { name: 'Save expense' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Enter an amount above 0.');
    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '50' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save expense' }));
    expect(await screen.findByText('Open a till shift first, or choose paid from elsewhere.')).toBeInTheDocument();
  });
});
