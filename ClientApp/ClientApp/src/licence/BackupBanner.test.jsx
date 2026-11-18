// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import BackupBanner from './BackupBanner';

const answer = (body) => vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => body })));
const renderBanner = () => render(<MemoryRouter><I18nProvider><BackupBanner /></I18nProvider></MemoryRouter>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Backup banner', () => {
  it('asks for the USB disk when no copy was made for days', async () => {
    answer({ available: true, warning: true, result: 'no-disk' });
    renderBanner();
    expect(await screen.findByText('Backups need attention.')).toBeInTheDocument();
    expect(screen.getByText(/Plug in the backup USB disk/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open backups' })).toHaveAttribute('href', '/admin/licence');
  });

  it('stays hidden when the backups are fresh, and on the cloud server', async () => {
    answer({ available: true, warning: false, result: 'copied' });
    const { container } = renderBanner();
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 20));
    expect(container).toBeEmptyDOMElement();
    cleanup();
    answer({ available: false, warning: false });
    const second = renderBanner();
    await new Promise((r) => setTimeout(r, 20));
    expect(second.container).toBeEmptyDOMElement();
  });
});
