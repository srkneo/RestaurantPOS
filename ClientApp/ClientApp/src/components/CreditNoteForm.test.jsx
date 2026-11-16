// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import CreditNoteForm from './CreditNoteForm';

const BILL = { id: 7, invoiceNumber: 'INV/26-27/000007', total: 533, amountPaid: 533, version: 2 };

function mockApi(response) {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    calls.push({ url, method: options?.method, body: options?.body ? JSON.parse(options.body) : undefined });
    return response;
  }));
  return calls;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('Credit note form', () => {
  it('needs a reason and a PIN, then makes the credit note', async () => {
    const note = { id: 3, number: 'CN/26-27/0000001' };
    const calls = mockApi({ ok: true, status: 201, json: async () => note });
    const onDone = vi.fn();
    render(<I18nProvider><CreditNoteForm bill={BILL} needPin onDone={onDone} onCancel={() => {}} /></I18nProvider>);

    fireEvent.click(screen.getByRole('button', { name: 'Make credit note' }));
    expect(screen.getByText('Give a reason.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Reason for credit note'), { target: { value: ' Food was cold ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Make credit note' }));
    expect(screen.getByText('Enter the manager PIN.')).toBeInTheDocument();
    expect(calls).toHaveLength(0);

    fireEvent.change(screen.getByLabelText('Money given back by'), { target: { value: 'Upi' } });
    fireEvent.change(screen.getByLabelText('Manager PIN'), { target: { value: '4321' } });
    fireEvent.click(screen.getByRole('button', { name: 'Make credit note' }));
    await waitFor(() => expect(onDone).toHaveBeenCalledWith(note));
    expect(calls[0]).toEqual({ url: '/api/bills/7/credit-note', method: 'POST',
      body: { reason: 'Food was cold', refundMethod: 'Upi', approverPin: '4321', version: 2 } });
  });

  it('asks no refund method for an unpaid bill and no PIN for a manager, and shows the server message', async () => {
    const calls = mockApi({ ok: false, status: 400, json: async () => ({ message: 'This bill is from today. Void it instead.' }) });
    render(<I18nProvider><CreditNoteForm bill={{ ...BILL, amountPaid: 0 }} needPin={false} onDone={() => {}} onCancel={() => {}} /></I18nProvider>);
    expect(screen.queryByLabelText('Money given back by')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Manager PIN')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Reason for credit note'), { target: { value: 'Duplicate bill' } });
    fireEvent.click(screen.getByRole('button', { name: 'Make credit note' }));
    expect(await screen.findByText('This bill is from today. Void it instead.')).toBeInTheDocument();
    expect(calls[0].body).toEqual({ reason: 'Duplicate bill', refundMethod: null, approverPin: null, version: 2 });
  });
});
