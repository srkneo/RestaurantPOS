// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import { clearAbout } from '../about/about';
import PlatformSupportPage from './PlatformSupportPage';

const saved = {
  companyName: 'Vyom Solutions', productName: 'RestaurantPOS', phone: null, whatsApp: null, email: null, hours: null, website: null
};

function mockApi(putAnswer = { ok: true, status: 200, body: null }) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    const method = options.method || 'GET';
    calls.push({ url, method, body: options.body ? JSON.parse(options.body) : null });
    if (method === 'PUT') return { ok: putAnswer.ok, status: putAnswer.status, json: async () => putAnswer.body ?? JSON.parse(options.body) };
    return { ok: true, status: 200, json: async () => saved };
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><PlatformSupportPage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  clearAbout();
  vi.unstubAllGlobals();
});

describe('Support details page', () => {
  it('loads and saves the details', async () => {
    const calls = mockApi();
    renderPage();
    expect(await screen.findByDisplayValue('Vyom Solutions')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Support phone'), { target: { value: ' 9429549007 ' } });
    fireEvent.change(screen.getByLabelText('Support email'), { target: { value: 'help@vyom.test' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Support details saved. Shop PCs get them with their next licence renewal.')).toBeInTheDocument();
    const put = calls.find((c) => c.method === 'PUT');
    expect(put.url).toBe('/api/platform/support');
    expect(put.body).toEqual({
      companyName: 'Vyom Solutions', productName: 'RestaurantPOS', phone: '9429549007', whatsApp: '', email: 'help@vyom.test', hours: '', website: ''
    });
  });

  it('asks for the company name before saving', async () => {
    const calls = mockApi();
    renderPage();
    fireEvent.change(await screen.findByLabelText('Company name'), { target: { value: '  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('This is needed.')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);
  });

  it('shows the server message next to the field', async () => {
    mockApi({ ok: false, status: 400, body: { errors: { email: ['Enter a valid email address.'] } } });
    renderPage();
    fireEvent.change(await screen.findByLabelText('Support email'), { target: { value: 'nope' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument();
  });
});
