// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import AboutPage from './AboutPage';
import AppFooter from './AppFooter';
import { clearAbout, whatsAppLink } from './about';

const about = (changes = {}) => ({
  companyName: 'Vyom Solutions', productName: 'RestaurantPOS', phone: '+91 94295 49007', whatsApp: null,
  email: 'help@vyom.test', hours: '8 AM - 10 AM', website: null, version: '1.7.2',
  shopPc: true, licenceCustomer: 'Anand Restaurant', licencePlan: 'trial', deviceCode: 'AB12CD34', ...changes
});

const answer = (body) => vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => body })));
const wrap = (node) => render(<MemoryRouter><I18nProvider><ToastProvider>{node}</ToastProvider></I18nProvider></MemoryRouter>);

afterEach(() => {
  cleanup();
  clearAbout();
  vi.unstubAllGlobals();
});

describe('About page', () => {
  it('shows who to call, the version and this PC', async () => {
    answer(about());
    wrap(<AboutPage />);
    expect(await screen.findByText('Made by Vyom Solutions')).toBeInTheDocument();
    expect(screen.getByText('v1.7.2')).toBeInTheDocument();
    expect(screen.getByText('help@vyom.test')).toBeInTheDocument();
    expect(screen.getByText('AB12CD34')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Call' })).toHaveAttribute('href', 'tel:+919429549007');
    expect(screen.getByRole('link', { name: 'WhatsApp' })).toHaveAttribute('href', 'https://wa.me/919429549007');
  });

  it('copies the details as one text', async () => {
    answer(about());
    const writeText = vi.fn(async () => {});
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    wrap(<AboutPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Copy details' }));
    await waitFor(() => expect(writeText).toHaveBeenCalled());
    const text = writeText.mock.calls[0][0];
    expect(text).toContain('RestaurantPOS');
    expect(text).toContain('Version: v1.7.2');
    expect(text).toContain('PC code: AB12CD34');
    expect(await screen.findByText('Details copied. Paste them in your message to support.')).toBeInTheDocument();
  });

  it('says when no contact is set up yet', async () => {
    answer(about({ phone: null, email: null }));
    wrap(<AboutPage />);
    expect(await screen.findByText('Support contact details are not set up yet.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'WhatsApp' })).not.toBeInTheDocument();
  });

  it('shows an error with a retry when it cannot load', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));
    wrap(<AboutPage />);
    expect(await screen.findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});

describe('Footer', () => {
  it('shows product, version, company and the support phone', async () => {
    answer(about());
    wrap(<AppFooter />);
    expect(await screen.findByText('RestaurantPOS v1.7.2')).toBeInTheDocument();
    expect(screen.getByText(/Vyom Solutions/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '+91 94295 49007' })).toHaveAttribute('href', 'tel:+919429549007');
    expect(screen.getByRole('link', { name: 'About' })).toHaveAttribute('href', '/about');
  });

  it('shows nothing when the server cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));
    const { container } = wrap(<AppFooter />);
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 20));
    expect(container.querySelector('footer')).toBeNull();
  });
});

describe('WhatsApp link', () => {
  it('adds the India code to a 10-digit number and keeps a full one', () => {
    expect(whatsAppLink('94295 49007')).toBe('https://wa.me/919429549007');
    expect(whatsAppLink('+971 50 123 4567')).toBe('https://wa.me/971501234567');
    expect(whatsAppLink('')).toBeNull();
  });
});
