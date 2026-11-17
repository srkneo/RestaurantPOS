// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import LicenceBanner from './LicenceBanner';

vi.mock('../AuthContext', () => ({ useAuth: () => ({ user: { roles: ['admin'] } }) }));

const answer = (body) => vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => body })));
const renderBanner = () => render(<MemoryRouter><I18nProvider><LicenceBanner /></I18nProvider></MemoryRouter>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Licence banner', () => {
  it('shows nothing when no licence is required', async () => {
    answer({ state: 'Active', locked: false, required: false, message: 'Licence not required.' });
    const { container } = renderBanner();
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 20));
    expect(container).toBeEmptyDOMElement();
  });

  it('shows nothing for an active licence', async () => {
    answer({ state: 'Active', locked: false, required: true, message: 'Licence active.' });
    const { container } = renderBanner();
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 20));
    expect(container).toBeEmptyDOMElement();
  });

  it('says billing is stopped when the PC is locked, with the reason and a link for the owner', async () => {
    answer({ state: 'LeaseExpired', locked: true, required: true, message: 'This PC has been offline for too long.' });
    renderBanner();
    expect(await screen.findByText('Billing is stopped on this PC.')).toBeInTheDocument();
    expect(screen.getByText('This PC has been offline for too long.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open licence page' })).toHaveAttribute('href', '/admin/licence');
  });

  it('warns without stopping billing when the licence ends soon', async () => {
    answer({ state: 'ExpiringSoon', locked: false, required: true, message: 'The licence ends on 20 Dec 2026. Contact support to renew.' });
    renderBanner();
    expect(await screen.findByText(/The licence ends on 20 Dec 2026/)).toBeInTheDocument();
    expect(screen.queryByText('Billing is stopped on this PC.')).not.toBeInTheDocument();
  });

  it('checks again when the licence changes on this PC', async () => {
    answer({ state: 'Missing', locked: true, required: true, message: 'This PC has no licence yet.' });
    renderBanner();
    expect(await screen.findByText('Billing is stopped on this PC.')).toBeInTheDocument();
    answer({ state: 'Active', locked: false, required: true, message: 'Licence active.' });
    window.dispatchEvent(new Event('licence-changed'));
    await waitFor(() => expect(screen.queryByText('Billing is stopped on this PC.')).not.toBeInTheDocument());
  });

  it('stays quiet when the server cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));
    const { container } = renderBanner();
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 20));
    expect(container).toBeEmptyDOMElement();
  });
});
