// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import LicenceMenuLink from './LicenceMenuLink';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Licence menu link', () => {
  it('links a shop licence to a cloud restaurant', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
      calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
      if (!options.method) return { ok: true, status: 200, json: async () => [{ id: 1, name: 'My Restaurant' }, { id: 2, name: 'Anand Cafe' }] };
      return { ok: true, status: 200, json: async () => ({}) };
    }));
    const onSaved = vi.fn();
    render(<I18nProvider><ToastProvider><LicenceMenuLink licence={{ id: 3, customer: 'Anand Cafe shop' }} onSaved={onSaved} /></ToastProvider></I18nProvider>);

    fireEvent.click(screen.getByRole('button', { name: 'Menu: own' }));
    expect(await screen.findByRole('option', { name: 'Anand Cafe' })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Copy the menu from this cloud business'), { target: { value: '2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save menu source' }));

    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    const post = calls.find((c) => c.method === 'POST');
    expect(post.url).toBe('/api/platform/licences/3/restaurant');
    expect(post.body).toEqual({ tenantId: 2 });
  });

  it('shows that a linked shop copies the cloud menu', () => {
    render(<I18nProvider><ToastProvider><LicenceMenuLink licence={{ id: 3, customer: 'X', tenantId: 2 }} /></ToastProvider></I18nProvider>);
    expect(screen.getByRole('button', { name: 'Menu: cloud' })).toBeInTheDocument();
  });
});
