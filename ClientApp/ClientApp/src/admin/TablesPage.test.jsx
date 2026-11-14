// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import TablesPage from './TablesPage';

const TABLES = [
  { id: 1, tableNumber: '1', isActive: true, sortOrder: 1 },
  { id: 2, tableNumber: 'Terrace', isActive: false, sortOrder: 2 },
];

function mockApi(write = { ok: true, status: 200, json: async () => ({}) }) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (options?.method) return write;
    return { ok: true, json: async () => TABLES };
  }));
  return calls;
}

const renderPage = () => render(<I18nProvider><ToastProvider><TablesPage /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Tables page', () => {
  it('lists tables with position and in-use status', async () => {
    mockApi();
    renderPage();
    expect(await screen.findByRole('heading', { name: 'Tables' })).toBeInTheDocument();
    const row = (await screen.findByText('Terrace')).closest('tr');
    expect(within(row).getByText('Not in use')).toBeInTheDocument();
    expect(within(row).getByText('2')).toBeInTheDocument();
  });

  it('adds a table after checking the number', async () => {
    const calls = mockApi({ ok: true, status: 201, json: async () => ({}) });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Add table' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(within(dialog).getByText('Enter a table number.')).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText('Table number or name'), { target: { value: 'Garden 1' } });
    fireEvent.change(within(dialog).getByLabelText('Position'), { target: { value: '11' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Table saved.')).toBeInTheDocument();
    const post = calls.find((c) => c.options?.method === 'POST');
    expect(post.url).toBe('/api/admin/tables');
    expect(JSON.parse(post.options.body)).toEqual({ tableNumber: 'Garden 1', sortOrder: 11, isActive: true });
  });

  it('marks a table not in use', async () => {
    const calls = mockApi();
    renderPage();
    const row = (await screen.findAllByRole('row'))[1];
    fireEvent.click(within(row).getByRole('button', { name: 'Edit' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByLabelText('In use'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Table saved.')).toBeInTheDocument();
    const put = calls.find((c) => c.options?.method === 'PUT');
    expect(put.url).toBe('/api/admin/tables/1');
    expect(JSON.parse(put.options.body)).toEqual({ tableNumber: '1', sortOrder: 1, isActive: false });
  });

  it('shows the server error next to the field', async () => {
    mockApi({ ok: false, status: 400, json: async () => ({ errors: { tablenumber: ['A table with this number already exists.'] } }) });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Add table' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Table number or name'), { target: { value: '1' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(await within(dialog).findByText('A table with this number already exists.')).toBeInTheDocument();
  });
});
