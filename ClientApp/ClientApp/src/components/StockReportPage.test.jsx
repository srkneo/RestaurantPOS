// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, within, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import StockReportPage from './StockReportPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const REPORT = {
  from: '2026-11-14', to: '2026-11-20', purchaseCost: 1100, wasteValue: 110, stockValue: 1265,
  items: [{ id: 1, name: 'Milk', unit: 'l', bought: 20, boughtCost: 1100, used: 5, wasted: 2, quantity: 23, costPerUnit: 55, value: 1265 }],
  suppliers: [{ supplier: 'Amul Dairy', purchases: 1, cost: 1100 }, { supplier: '', purchases: 2, cost: 0 }],
};

describe('Stock report page', () => {
  it('shows totals, items and suppliers, and loads the chosen days', async () => {
    const urls = [];
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      urls.push(url);
      return { ok: true, status: 200, json: async () => REPORT };
    }));
    render(<I18nProvider><StockReportPage /></I18nProvider>);
    const summary = await screen.findByRole('region', { name: 'Summary' });
    expect(within(summary).getByText('₹1100.00')).toBeInTheDocument();
    expect(within(summary).getByText('₹110.00')).toBeInTheDocument();
    const items = screen.getByRole('region', { name: 'Items' });
    expect(within(items).getByText('20 l')).toBeInTheDocument();
    expect(within(items).getByText('23 l')).toBeInTheDocument();
    const suppliers = screen.getByRole('region', { name: 'Suppliers' });
    expect(within(suppliers).getByText('Amul Dairy')).toBeInTheDocument();
    expect(within(suppliers).getByText('Not given')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-11-01' } });
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-11-07' } });
    fireEvent.click(screen.getByRole('button', { name: 'Show' }));
    await vi.waitFor(() => expect(urls).toContain('/api/stock/report?from=2026-11-01&to=2026-11-07'));
  });

  it('shows the server message for a bad range', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: false, status: 400, json: async () => ({ message: 'Choose at most 92 days.' }) })));
    render(<I18nProvider><StockReportPage /></I18nProvider>);
    expect(await screen.findByRole('alert')).toHaveTextContent('Choose at most 92 days.');
  });
});
