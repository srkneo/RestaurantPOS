// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import MenuScanPage from './MenuScanPage';

const SCAN = {
  scansLeftToday: 19,
  items: [
    { category: 'Starters', name: 'Paneer Tikka', price: 220, isVeg: true, description: null, alreadyOnMenu: false },
    { category: 'Sides', name: 'Fries', price: 90, isVeg: true, description: null, alreadyOnMenu: true },
  ],
};

function mockApi({ scan = { ok: true, status: 200, json: async () => SCAN }, save = { ok: true, status: 200, json: async () => ({ created: 1, skipped: 0, categoriesCreated: 1 }) } } = {}) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, options });
    if (url === '/api/admin/menu/scan') return scan;
    if (url === '/api/admin/menu/import') return save;
    return { ok: false, status: 404, json: async () => ({}) };
  }));
  return calls;
}

const renderPage = () => render(
  <MemoryRouter initialEntries={['/admin/menu/scan']}>
    <I18nProvider>
      <ToastProvider>
        <Routes>
          <Route path="/admin/menu/scan" element={<MenuScanPage />} />
          <Route path="/admin/menu" element={<p>Menu page</p>} />
        </Routes>
      </ToastProvider>
    </I18nProvider>
  </MemoryRouter>
);

const photo = (name = 'page1.jpg') => new File(['photo'], name, { type: 'image/jpeg' });

async function scanOnePhoto() {
  fireEvent.change(screen.getByLabelText('Menu photos'), { target: { files: [photo()] } });
  fireEvent.click(screen.getByRole('button', { name: 'Read menu' }));
  return screen.findByRole('heading', { name: 'Check the items' });
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Menu scan page', () => {
  it('needs 1 to 4 photos before reading', async () => {
    mockApi();
    renderPage();
    expect(screen.getByRole('heading', { name: 'Scan a paper menu' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Read menu' })).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Menu photos'), { target: { files: [photo('1.jpg'), photo('2.jpg'), photo('3.jpg'), photo('4.jpg'), photo('5.jpg')] } });
    expect(screen.getByText('Choose up to 4 photos.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Read menu' })).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Menu photos'), { target: { files: [photo('1.jpg'), photo('2.jpg')] } });
    expect(screen.getByText('2 photos chosen')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Read menu' })).toBeEnabled();
  });

  it('sends the photos and shows the found items for checking', async () => {
    const calls = mockApi();
    renderPage();
    await scanOnePhoto();

    const scan = calls.find((c) => c.url === '/api/admin/menu/scan');
    expect(scan.options.method).toBe('POST');
    expect(scan.options.body).toBeInstanceOf(FormData);
    expect(scan.options.body.getAll('photos')).toHaveLength(1);

    expect(screen.getByLabelText('Name 1')).toHaveValue('Paneer Tikka');
    expect(screen.getByLabelText('Price (₹) 2')).toHaveValue('90');
    expect(screen.getByText('Already on menu')).toBeInTheDocument();
    expect(screen.getByText('19 scans left today.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add 1 to menu' })).toBeEnabled();
  });

  it('adds the checked rows with the chosen GST rate, then goes back to the menu', async () => {
    const calls = mockApi();
    renderPage();
    await scanOnePhoto();

    fireEvent.change(screen.getByLabelText('Price (₹) 1'), { target: { value: '240' } });
    fireEvent.change(screen.getByLabelText('GST rate (%)'), { target: { value: '12' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add 1 to menu' }));

    expect(await screen.findByText('Menu page')).toBeInTheDocument();
    expect(screen.getByText('1 items added, 0 skipped.')).toBeInTheDocument();
    const save = calls.find((c) => c.url === '/api/admin/menu/import');
    expect(save.options.method).toBe('POST');
    expect(JSON.parse(save.options.body)).toEqual({
      taxRate: 12,
      items: [{ category: 'Starters', name: 'Paneer Tikka', price: 240, isVeg: true, description: null }],
    });
  });

  it('points at the row with a bad price and saves nothing', async () => {
    const calls = mockApi();
    renderPage();
    await scanOnePhoto();

    fireEvent.change(screen.getByLabelText('Price (₹) 1'), { target: { value: 'abc' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add 1 to menu' }));

    expect(screen.getByText('Row 1: enter a price between 0 and 100000.')).toBeInTheDocument();
    expect(calls.some((c) => c.url === '/api/admin/menu/import')).toBe(false);
  });

  it('can tick an item that was already on the menu and add a row by hand', async () => {
    mockApi();
    renderPage();
    await scanOnePhoto();

    fireEvent.click(screen.getByLabelText('Add Fries'));
    expect(screen.getByRole('button', { name: 'Add 2 to menu' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add a row' }));
    expect(screen.getByLabelText('Name 3')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Add 3 to menu' })).toBeInTheDocument();
  });

  it('shows the server message when the scan cannot run', async () => {
    mockApi({ scan: { ok: false, status: 503, json: async () => ({ title: 'Menu scan is not set up on this server.' }) } });
    renderPage();
    fireEvent.change(screen.getByLabelText('Menu photos'), { target: { files: [photo()] } });
    fireEvent.click(screen.getByRole('button', { name: 'Read menu' }));

    expect(await screen.findByText('Menu scan is not set up on this server.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Read menu' })).toBeInTheDocument();
  });

  it('reads a CSV file into the same check list', async () => {
    mockApi();
    renderPage();
    const csv = new File(['x'], 'menu.csv', { type: 'text/csv' });
    Object.defineProperty(csv, 'text', { value: async () => 'category,name,price,veg\nDrinks,Masala Chai,20,yes\n' });
    fireEvent.change(screen.getByLabelText('CSV file'), { target: { files: [csv] } });

    expect(await screen.findByRole('heading', { name: 'Check the items' })).toBeInTheDocument();
    expect(screen.getByLabelText('Name 1')).toHaveValue('Masala Chai');
    expect(screen.getByLabelText('Category 1')).toHaveValue('Drinks');
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(2);
  });
});
