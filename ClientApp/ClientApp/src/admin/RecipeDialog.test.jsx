// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import RecipeDialog from './RecipeDialog';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const stock = [
  { id: 1, name: 'Milk', unit: 'l', quantity: 10, lowLevel: 0, costPerUnit: null, isLow: false },
  { id: 2, name: 'Sugar', unit: 'kg', quantity: 2, lowLevel: 0, costPerUnit: null, isLow: false },
];

const mock = (recipe, putStatus = 200, putBody = []) => {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    calls.push({ url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null });
    if (options.method === 'PUT') return { ok: putStatus < 300, status: putStatus, json: async () => putBody };
    if (url === '/api/stock') return { ok: true, status: 200, json: async () => stock };
    return { ok: true, status: 200, json: async () => recipe };
  }));
  return calls;
};

const show = (onClose = () => {}) => render(
  <I18nProvider><ToastProvider><RecipeDialog item={{ id: 7, name: 'Masala Chai' }} onClose={onClose} /></ToastProvider></I18nProvider>,
);

describe('Recipe dialog', () => {
  it('shows the saved recipe and saves an added row', async () => {
    const calls = mock([{ stockItemId: 1, name: 'Milk', unit: 'l', quantity: 0.15 }]);
    const onClose = vi.fn();
    show(onClose);
    expect(await screen.findByLabelText('Stock item 1')).toHaveValue('1');
    expect(screen.getByLabelText('Quantity 1')).toHaveValue('0.15');
    expect(screen.getByText('l')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Add stock item' }));
    fireEvent.change(screen.getByLabelText('Stock item 2'), { target: { value: '2' } });
    fireEvent.change(screen.getByLabelText('Quantity 2'), { target: { value: '0.01' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save recipe' }));

    expect(await screen.findByText('Recipe of Masala Chai saved.')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'PUT')).toMatchObject({
      url: '/api/menu/7/recipe',
      body: { lines: [{ stockItemId: 1, quantity: 0.15 }, { stockItemId: 2, quantity: 0.01 }] },
    });
    expect(onClose).toHaveBeenCalled();
  });

  it('asks for a quantity in every row and shows server errors', async () => {
    mock([], 400, { errors: { lines: ['Choose stock items from this outlet\'s stock list.'] } });
    show();
    fireEvent.change(await screen.findByLabelText('Stock item 1'), { target: { value: '1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save recipe' }));
    expect(await screen.findByText('Choose a stock item and a quantity above 0 in every row.')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Quantity 1'), { target: { value: '1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save recipe' }));
    expect(await screen.findByText('Choose stock items from this outlet\'s stock list.')).toBeInTheDocument();
  });
});
