// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import FeedbackListPage from './FeedbackListPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Feedback list page', () => {
  it('shows the average and every rating with its comment', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({
      ok: true, status: 200, json: async () => ({
        count: 2, average: 4.5, items: [
          { id: 2, invoiceNumber: 'INV/26-27/000008', rating: 5, comment: 'Great masala tea', createdAt: '2026-11-18T10:00:00Z' },
          { id: 1, invoiceNumber: 'INV/26-27/000007', rating: 4, comment: null, createdAt: '2026-11-18T09:00:00Z' },
        ],
      }),
    })));
    render(<I18nProvider initialLang="en"><FeedbackListPage /></I18nProvider>);
    expect(await screen.findByText('Average 4.5 of 5 from 2 guests')).toBeInTheDocument();
    expect(screen.getByText('Great masala tea')).toBeInTheDocument();
    expect(screen.getByLabelText('5 stars')).toBeInTheDocument();
    expect(screen.getByLabelText('4 stars')).toBeInTheDocument();
  });

  it('says when there is no feedback yet', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => ({ count: 0, average: null, items: [] }) })));
    render(<I18nProvider initialLang="en"><FeedbackListPage /></I18nProvider>);
    expect(await screen.findByText(/No feedback yet/)).toBeInTheDocument();
  });
});
