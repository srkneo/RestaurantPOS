// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import PricingPage from './PricingPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const mock = (plans, about) => vi.stubGlobal('fetch', vi.fn(async (url) => {
  const body = url === '/api/signup/plans' ? plans : url === '/api/about' ? about : null;
  return body ? { ok: true, status: 200, json: async () => body } : { ok: false, status: 404, json: async () => null };
}));

const show = () => render(<MemoryRouter><I18nProvider><PricingPage /></I18nProvider></MemoryRouter>);

describe('Pricing page', () => {
  it('shows both plans, the yearly saving and the trial button', async () => {
    mock({ trialDays: 30, monthly: 1000, yearly: 10000, signupOpen: true }, { whatsApp: '98250 12345', phone: '+91 98250 12345' });
    show();
    expect(await screen.findByText('₹1,000 a month')).toBeInTheDocument();
    expect(screen.getByText('₹10,000 a year')).toBeInTheDocument();
    expect(screen.getByText(/Save ₹2,000/)).toBeInTheDocument();
    expect(screen.getByText(/30 days free trial/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Start free trial' })).toHaveAttribute('href', '/signup');
    expect(await screen.findByRole('link', { name: 'Ask for a demo on WhatsApp' })).toHaveAttribute('href', expect.stringContaining('https://wa.me/919825012345?text='));
    expect(screen.getByRole('link', { name: 'Ask for a demo on WhatsApp' })).toHaveAttribute('href', expect.stringContaining(encodeURIComponent('Hello, I would like a demo of AllInOne POS.')));
    expect(screen.getByRole('link', { name: 'Call +91 98250 12345' })).toHaveAttribute('href', 'tel:+919825012345');
  });

  it('hides the trial and WhatsApp buttons when signup is closed and no WhatsApp number is set', async () => {
    mock({ trialDays: 30, monthly: 1000, yearly: 10000, signupOpen: false }, { whatsApp: null, phone: null });
    show();
    expect(await screen.findByText('₹1,000 a month')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Start free trial' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Ask for a demo on WhatsApp' })).not.toBeInTheDocument();
  });
});
