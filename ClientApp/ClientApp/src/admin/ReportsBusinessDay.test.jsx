// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import ReportsPage from './ReportsPage';

const report = (from, to) => ({
  from, to, billCount: 0, itemsTotal: 0, discount: 0, taxableValue: 0, cgst: 0, sgst: 0, total: 0, creditTotal: 0, netSales: 0,
  days: [], items: [], categories: [], payments: [], hours: [], staff: [], taxSummary: [], expenses: [], expenseTotal: 0,
  feedbackCount: 0, feedbackAverage: null,
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Reports page business day', () => {
  it('starts on the business day the server gives and counts quick ranges from it', async () => {
    const urls = [];
    vi.stubGlobal('fetch', vi.fn(async (url) => {
      urls.push(url);
      const m = /from=([\d-]+)&to=([\d-]+)/.exec(url);
      return { ok: true, status: 200, json: async () => (m ? report(m[1], m[2]) : report('2026-11-19', '2026-11-19')) };
    }));
    render(<I18nProvider><ReportsPage /></I18nProvider>);

    // No dates on the first call: the server answers with the restaurant's business day (before 4 AM it is still yesterday).
    await vi.waitFor(() => expect(screen.getByLabelText('From')).toHaveValue('2026-11-19'));
    expect(urls[0]).toBe('/api/reports/sales');
    expect(screen.getByLabelText('To')).toHaveValue('2026-11-19');

    fireEvent.click(screen.getByRole('button', { name: 'Last 7 days' }));
    await vi.waitFor(() => expect(urls).toContain('/api/reports/sales?from=2026-11-13&to=2026-11-19'));
    fireEvent.click(screen.getByRole('button', { name: 'This month' }));
    await vi.waitFor(() => expect(urls).toContain('/api/reports/sales?from=2026-11-01&to=2026-11-19'));
  });
});
