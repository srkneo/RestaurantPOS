// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import ComboDialog from './ComboDialog';

const MEAL = { id: 1, name: 'Burger Meal' };
const ITEMS = [MEAL, { id: 2, name: 'Burger' }, { id: 3, name: 'Fries' }];

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Combo dialog', () => {
  it('saves the items a combo includes', async () => {
    const calls = [];
    vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
      calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
      if (options.method === 'PUT') return { ok: true, status: 200, json: async () => [] };
      return { ok: true, status: 200, json: async () => [{ partItemId: 2, name: 'Burger', quantity: 1 }] };
    }));
    const onClose = vi.fn();
    render(<I18nProvider><ToastProvider><ComboDialog item={MEAL} items={ITEMS} onClose={onClose} /></ToastProvider></I18nProvider>);

    expect(await screen.findByLabelText('Item 1')).toHaveValue('2');
    expect(screen.queryByRole('option', { name: 'Burger Meal' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add item' }));
    fireEvent.change(screen.getByLabelText('Item 2'), { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText('Quantity of item 2'), { target: { value: '2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(onClose).toHaveBeenCalledWith(true));
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ parts: [{ partItemId: 2, quantity: 1 }, { partItemId: 3, quantity: 2 }] });
  });
});
