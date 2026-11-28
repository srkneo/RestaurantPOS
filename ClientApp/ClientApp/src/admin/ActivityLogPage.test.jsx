// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import ActivityLogPage, { PlatformActivityLogPage } from './ActivityLogPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const ROWS = [
  { id: 3, createdAt: '2026-11-28T10:00:00Z', by: 'owner@pos.local', action: 'Changed', entityType: 'MenuItem', entityId: '5', details: '{"changes":{"Price":[18,20]}}', support: false },
  { id: 2, createdAt: '2026-11-28T09:00:00Z', by: 'support-1@pos.local', action: 'SupportLogin', entityType: 'Support', entityId: null, details: '{"support":"Support session started by mahesh@vyom.in"}', support: true },
  { id: 1, createdAt: '2026-11-28T08:00:00Z', by: 'system', action: 'Created', entityType: 'MenuCategory', entityId: '2', details: '{"changes":{"Name":[null,"Snacks"]}}', support: false },
];

const answer = (body, ok = true, status = 200) => vi.fn(async () => ({ ok, status, json: async () => body }));
const renderPage = (props = {}) => render(<I18nProvider initialLang="en"><ActivityLogPage {...props} /></I18nProvider>);

describe('Activity log page', () => {
  it('shows who, when and what, newest first, with readable details', async () => {
    vi.stubGlobal('fetch', answer(ROWS));
    renderPage();
    expect(await screen.findByRole('heading', { name: 'Activity log' })).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith('/api/admin/audit?take=500');
    const rows = await screen.findAllByRole('row');
    expect(rows).toHaveLength(4);
    expect(within(rows[0]).getByText('When')).toBeInTheDocument();
    expect(within(rows[0]).getByText('Who')).toBeInTheDocument();
    expect(within(rows[0]).getByText('What')).toBeInTheDocument();
    expect(within(rows[0]).getByText('Details')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Price: 18 → 20')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Changed · Menu item #5')).toBeInTheDocument();
    expect(within(rows[1]).getByText('owner@pos.local')).toBeInTheDocument();
    expect(within(rows[2]).getByText('Support sign-in · Support')).toBeInTheDocument();
    expect(within(rows[3]).getByText('Name: Snacks')).toBeInTheDocument();
    expect(within(rows[3]).getByText('Created · Menu category #2')).toBeInTheDocument();
  });

  it('highlights support sessions', async () => {
    vi.stubGlobal('fetch', answer(ROWS));
    renderPage();
    const supportRow = (await screen.findByText('Support session started by mahesh@vyom.in')).closest('tr');
    expect(supportRow).toHaveClass('table-warning');
    expect(within(supportRow).getByText('Support')).toBeInTheDocument();
    expect(screen.getByText('Price: 18 → 20').closest('tr')).not.toHaveClass('table-warning');
  });

  it('filters by type', async () => {
    vi.stubGlobal('fetch', answer(ROWS));
    renderPage();
    const select = await screen.findByLabelText('Type');
    const options = within(select).getAllByRole('option').map((o) => o.textContent);
    expect(options).toEqual(['All types', 'Menu category', 'Menu item', 'Support']);
    fireEvent.change(select, { target: { value: 'Support' } });
    expect(screen.queryByText('Price: 18 → 20')).not.toBeInTheDocument();
    expect(screen.getByText('Support session started by mahesh@vyom.in')).toBeInTheDocument();
    fireEvent.change(select, { target: { value: 'all' } });
    expect(screen.getByText('Price: 18 → 20')).toBeInTheDocument();
  });

  it('shows an unknown action and details that are not JSON as they are', async () => {
    vi.stubGlobal('fetch', answer([{ id: 9, createdAt: '2026-11-28T10:00:00Z', by: 'system', action: 'Merged', entityType: 'Bill', entityId: null, details: 'plain old text', support: false }]));
    renderPage();
    expect(await screen.findByText('plain old text')).toBeInTheDocument();
    expect(screen.getByText('Merged · Bill')).toBeInTheDocument();
  });

  it('says when nothing has been changed yet', async () => {
    vi.stubGlobal('fetch', answer([]));
    renderPage();
    expect(await screen.findByText('Nothing has been changed yet.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('shows an error with a retry when the server refuses', async () => {
    vi.stubGlobal('fetch', answer({}, false, 403));
    renderPage();
    expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong. Please try again.');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('pages after 20 rows', async () => {
    const many = Array.from({ length: 25 }, (_, i) => ({ id: 25 - i, createdAt: '2026-11-28T10:00:00Z', by: 'owner@pos.local', action: 'Changed', entityType: 'MenuItem', entityId: String(i), details: `{"changes":{"Price":[${i},${i + 1}]}}`, support: false }));
    vi.stubGlobal('fetch', answer(many));
    renderPage();
    await screen.findByText('Price: 0 → 1');
    expect(screen.getAllByRole('row')).toHaveLength(21);
    expect(screen.queryByText('Price: 24 → 25')).not.toBeInTheDocument();
  });

  it('reads one restaurant for the platform owner, with a back link', async () => {
    vi.stubGlobal('fetch', answer(ROWS));
    render(
      <I18nProvider initialLang="en">
        <MemoryRouter initialEntries={['/platform/tenants/7/activity']}>
          <Routes><Route path="/platform/tenants/:id/activity" element={<PlatformActivityLogPage />} /></Routes>
        </MemoryRouter>
      </I18nProvider>,
    );
    expect(await screen.findByText('Price: 18 → 20')).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith('/api/platform/tenants/7/audit?take=500');
    expect(screen.getByRole('link', { name: 'Back' })).toHaveAttribute('href', '/platform');
  });
});
