// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import WeightDialog, { measureText } from './WeightDialog';

afterEach(() => cleanup());

const PANEER = { id: 4, name: 'Paneer', price: 400, sellUnit: 'kg' };
const CHAAS = { id: 5, name: 'Chaas', price: 60, sellUnit: 'litre' };

const open = (item, onAdd = vi.fn(), onCancel = vi.fn()) => {
  const view = render(<I18nProvider><WeightDialog item={item} onAdd={onAdd} onCancel={onCancel} /></I18nProvider>);
  return { onAdd, onCancel, view };
};

describe('measureText', () => {
  it('shows grams and ml below 1, kg and L from 1', () => {
    expect(measureText(0.25, 'kg')).toBe('250 g');
    expect(measureText(0.5, 'litre')).toBe('500 ml');
    expect(measureText(1, 'kg')).toBe('1 kg');
    expect(measureText(1.5, 'litre')).toBe('1.5 L');
    expect(measureText(0.3333, 'kg')).toBe('333 g');
  });
});

describe('Weight dialog', () => {
  it('shows nothing when no item is being weighed', () => {
    const { view } = open(null);
    expect(view.container).toBeEmptyDOMElement();
  });

  it('adds a quick amount in one tap', () => {
    const { onAdd } = open(PANEER);
    expect(screen.getByRole('dialog', { name: 'Paneer' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '250 g' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '1 kg' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '500 g' }));
    expect(onAdd).toHaveBeenCalledWith(0.5);
  });

  it('offers ml and L for items sold by the litre', () => {
    open(CHAAS);
    expect(screen.getByRole('button', { name: '250 ml' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '500 ml' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '1 L' })).toBeInTheDocument();
  });

  it('adds a typed amount', () => {
    const { onAdd } = open(PANEER);
    fireEvent.change(screen.getByLabelText('Amount in kg'), { target: { value: '1.25' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onAdd).toHaveBeenCalledWith(1.25);
  });

  it('refuses zero or more than 100', () => {
    const { onAdd } = open(CHAAS);
    fireEvent.change(screen.getByLabelText('Amount in litre'), { target: { value: '0' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(screen.getByText('Enter more than 0 and at most 100')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Amount in litre'), { target: { value: '101' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onAdd).not.toHaveBeenCalled();
  });

  it('cancels', () => {
    const { onCancel } = open(PANEER);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalled();
  });
});
