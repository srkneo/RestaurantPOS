// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import MenuOptionsDialog from './MenuOptionsDialog';

const ITEM = { id: 10, name: 'Pizza' };

function mockApi(saved = { ok: true, status: 200, json: async () => ({}) }) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
    if (options.method === 'PUT') return saved;
    return { ok: true, status: 200, json: async () => ({ variants: [{ id: 1, name: 'Regular', price: 250, isAvailable: true }], addOns: [] }) };
  }));
  return calls;
}

const renderDialog = (onClose = vi.fn()) => {
  render(<I18nProvider><ToastProvider><MenuOptionsDialog item={ITEM} onClose={onClose} /></ToastProvider></I18nProvider>);
  return onClose;
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Sizes and extras dialog', () => {
  it('adds a size and an extra and saves them', async () => {
    const calls = mockApi();
    const onClose = renderDialog();
    expect(await screen.findByDisplayValue('Regular')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: '+ Add size' }));
    fireEvent.change(screen.getByLabelText('Sizes 2: name'), { target: { value: 'Large' } });
    fireEvent.change(screen.getByLabelText('Sizes 2: price'), { target: { value: '350' } });
    fireEvent.click(screen.getByRole('button', { name: '+ Add extra' }));
    fireEvent.change(screen.getByLabelText('Extras 1: name'), { target: { value: 'Extra cheese' } });
    fireEvent.change(screen.getByLabelText('Extras 1: price'), { target: { value: '30' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(onClose).toHaveBeenCalledWith(true));
    expect(calls.find((c) => c.method === 'PUT')).toEqual({
      url: '/api/admin/menu/items/10/options', method: 'PUT',
      body: {
        variants: [{ id: 1, name: 'Regular', price: 250, isAvailable: true }, { id: null, name: 'Large', price: 350, isAvailable: true }],
        addOns: [{ id: null, name: 'Extra cheese', price: 30, isAvailable: true }],
      },
    });
  });

  it('asks for a name and a price before saving', async () => {
    const calls = mockApi();
    renderDialog();
    await screen.findByDisplayValue('Regular');
    fireEvent.click(screen.getByRole('button', { name: '+ Add extra' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Every size and extra needs a name.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Extras 1: name'), { target: { value: 'Olives' } });
    fireEvent.change(screen.getByLabelText('Extras 1: price'), { target: { value: 'abc' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Enter each price as a number, like 30 or 12.50.')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);
  });

  it('removes a row', async () => {
    const calls = mockApi();
    renderDialog();
    await screen.findByDisplayValue('Regular');
    fireEvent.click(screen.getByRole('button', { name: 'Remove Sizes 1' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await vi.waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ variants: [], addOns: [] });
  });
});
