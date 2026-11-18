// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import FeedbackPage from './FeedbackPage';

function mockApi({ form = { restaurantName: 'Anand Thali House', invoiceNumber: 'INV/26-27/000007', alreadyGiven: false }, post = { status: 200 }, getStatus = 200 } = {}) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, method: options?.method ?? 'GET', body: options?.body ? JSON.parse(options.body) : undefined });
    if (options?.method === 'POST') return { ok: post.status === 200, status: post.status, json: async () => ({}) };
    return { ok: getStatus === 200, status: getStatus, json: async () => form };
  }));
  return calls;
}

const renderPage = () => render(
  <MemoryRouter initialEntries={['/feedback/abc123']}>
    <I18nProvider initialLang="en">
      <Routes><Route path="/feedback/:token" element={<FeedbackPage />} /></Routes>
    </I18nProvider>
  </MemoryRouter>
);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Guest feedback page', () => {
  it('sends the stars and the comment, then thanks the guest', async () => {
    const calls = mockApi();
    renderPage();
    expect(await screen.findByRole('heading', { name: 'Anand Thali House' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Send feedback' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Tap the stars to rate your visit.');

    fireEvent.click(screen.getByRole('button', { name: '4 stars' }));
    fireEvent.change(screen.getByLabelText('Anything to tell us? (optional)'), { target: { value: 'Tasty dal' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send feedback' }));

    expect(await screen.findByText('Thank you for your feedback!')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST')).toEqual({ url: '/api/feedback/abc123', method: 'POST', body: { rating: 4, comment: 'Tasty dal' } });
  });

  it('says so when this bill already has feedback, or the link does not work', async () => {
    mockApi({ form: { restaurantName: 'Cafe', invoiceNumber: 'INV/1', alreadyGiven: true } });
    renderPage();
    expect(await screen.findByText('Thank you, we already have your feedback for this bill.')).toBeInTheDocument();
    cleanup();
    mockApi({ getStatus: 404 });
    renderPage();
    expect(await screen.findByText('This feedback link does not work any more.')).toBeInTheDocument();
  });
});
