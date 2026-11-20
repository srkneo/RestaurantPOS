// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider } from '../ui/ToastContext';
import ReservationsPage from './ReservationsPage';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const booked = { id: 5, customerName: 'Patel family', phone: '98250 12345', people: 4, date: '2026-11-21', time: '20:00', tableId: 7, tableNumber: '7', note: 'Birthday', status: 'Booked' };

const mock = (handler) => {
  const calls = [];
  vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
    const call = { url, method: options.method || 'GET', body: options.body ? JSON.parse(options.body) : null };
    calls.push(call);
    if (url === '/api/tables') return { ok: true, status: 200, json: async () => [{ id: 7, tableNumber: '7', status: 'Free' }] };
    const [status, body] = handler(call);
    return { ok: status < 300, status, json: async () => body };
  }));
  return calls;
};

const show = () => render(<I18nProvider><ToastProvider><ReservationsPage /></ToastProvider></I18nProvider>);

describe('Reservations page', () => {
  it('lists the day and seats a booking', async () => {
    let list = [booked];
    const calls = mock((c) => {
      if (c.method === 'POST') {
        list = [{ ...booked, status: 'Seated' }];
        return [200, list[0]];
      }
      return [200, list];
    });
    show();
    expect(await screen.findByText('Patel family')).toBeInTheDocument();
    expect(screen.getByText('Table 7')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Seat Patel family' }));
    expect(await screen.findByText('Patel family: Seated.')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST')).toMatchObject({ url: '/api/reservations/5/status', body: { status: 'Seated' } });
    expect(await screen.findByRole('button', { name: 'Undo Patel family' })).toBeInTheDocument();
  });

  it('books a table and shows server errors next to the field', async () => {
    let fail = true;
    const calls = mock((c) => {
      if (c.method === 'POST' && fail) {
        fail = false;
        return [400, { errors: { tableId: ['Table 7 is already booked at 20:30 for Shah.'] } }];
      }
      if (c.method === 'POST') return [201, { ...booked, id: 6, customerName: 'Mehta', date: c.body.date }];
      return [200, []];
    });
    show();
    expect(await screen.findByText('No reservations for this day.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'New reservation' }));
    fireEvent.change(screen.getByLabelText('Guest name'), { target: { value: 'Mehta' } });
    fireEvent.change(screen.getByLabelText('People'), { target: { value: '6' } });
    fireEvent.change(screen.getByLabelText('Table'), { target: { value: '7' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Table 7 is already booked at 20:30 for Shah.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Booked: Mehta at 20:00.')).toBeInTheDocument();
    const post = calls.filter((c) => c.method === 'POST').pop();
    expect(post.body).toMatchObject({ customerName: 'Mehta', people: 6, time: '20:00', tableId: 7, phone: null, note: null });
  });
});
