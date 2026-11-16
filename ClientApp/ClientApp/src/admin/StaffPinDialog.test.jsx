// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import StaffPinDialog from './StaffPinDialog';

const MEENA = { id: 'u1', displayName: 'Meena', role: 'manager', hasPin: true };

function mockApi(response) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, method: options?.method, body: options?.body ? JSON.parse(options.body) : undefined });
    return response;
  }));
  return calls;
}

const renderDialog = (props) => render(<I18nProvider><ToastProvider><StaffPinDialog staff={MEENA} onClose={() => {}} onSaved={() => {}} {...props} /></ToastProvider></I18nProvider>);

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Staff PIN dialog', () => {
  it('saves a new PIN', async () => {
    const calls = mockApi({ ok: true, status: 204, json: async () => ({}) });
    const onSaved = vi.fn();
    const onClose = vi.fn();
    renderDialog({ onSaved, onClose });
    expect(screen.getByRole('heading', { name: 'PIN for Meena' })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Approval PIN'), { target: { value: ' 4321 ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('PIN saved.')).toBeInTheDocument();
    expect(calls[0]).toEqual({ url: '/api/staff/u1/pin', method: 'PUT', body: { pin: '4321' } });
    expect(onSaved).toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
  });

  it('removes the PIN', async () => {
    const calls = mockApi({ ok: true, status: 204, json: async () => ({}) });
    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Remove PIN' }));
    await waitFor(() => expect(calls[0]?.body).toEqual({ pin: '' }));
  });

  it('shows the server error next to the field', async () => {
    mockApi({ ok: false, status: 400, json: async () => ({ errors: { pin: ['Use 4 to 6 digits.'] } }) });
    renderDialog();
    fireEvent.change(screen.getByLabelText('Approval PIN'), { target: { value: '12' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Use 4 to 6 digits.')).toBeInTheDocument();
  });
});
