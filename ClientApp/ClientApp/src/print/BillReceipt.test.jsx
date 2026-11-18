// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import BillReceipt from './BillReceipt';
import KotTicket from './KotTicket';
import { billDateTime, halfRate } from './printText';

const STORE = { restaurantName: 'Anand Thali House', outletName: 'Station Road', address: 'Station Road, Anand', phone: '9876543210', gstin: '24ABCDE1234F1Z5', fssai: '10720011000123', footer: 'Follow us @anandthali' };

const GST_BILL = {
  id: 7, invoiceNumber: 'INV/26-27/000007', taxMode: 'Regular', taxNote: null, issuedAt: '2026-11-15T06:30:00Z', status: 'Unpaid',
  orderLabel: 'Table 5', tokenNumber: 12, customerName: null,
  itemsTotal: 500, discount: 0, taxableValue: 500, cgst: 16.4, sgst: 16.4, roundOff: 0.2, total: 533, amountDue: 533, printCount: 0,
  store: STORE,
  lines: [
    { itemName: 'Paneer Tikka', quantity: 2, unitPrice: 220, amount: 440 },
    { itemName: 'Cola', quantity: 1, unitPrice: 60, amount: 60 },
  ],
  taxSummary: [{ taxRate: 5, taxableValue: 440, cgst: 11, sgst: 11 }, { taxRate: 18, taxableValue: 60, cgst: 5.4, sgst: 5.4 }],
  payments: [],
};

afterEach(cleanup);

describe('Bill receipt', () => {
  it('prints the feedback QR when the outlet asks guests for feedback', () => {
    render(<BillReceipt bill={GST_BILL} feedbackQrUrl="/api/bills/7/feedback-qr.svg?origin=x" />);
    expect(screen.getByAltText('Tell us how we did')).toHaveAttribute('src', '/api/bills/7/feedback-qr.svg?origin=x');
  });

  it('prints the Google review QR when the outlet has a review link', () => {
    const { unmount } = render(<BillReceipt bill={GST_BILL} reviewQrUrl="/api/review-qr.svg" />);
    expect(screen.getByAltText('Rate us on Google')).toHaveAttribute('src', '/api/review-qr.svg');
    unmount();
    render(<BillReceipt bill={GST_BILL} />);
    expect(screen.queryByAltText('Rate us on Google')).not.toBeInTheDocument();
  });

  it('shows the store, GST numbers, lines, tax per rate and the rounded total', () => {
    render(<BillReceipt bill={GST_BILL} />);
    expect(screen.getByText('Anand Thali House')).toBeInTheDocument();
    expect(screen.getByText('GSTIN: 24ABCDE1234F1Z5')).toBeInTheDocument();
    expect(screen.getByText('FSSAI: 10720011000123')).toBeInTheDocument();
    expect(screen.getByText('TAX INVOICE')).toBeInTheDocument();
    expect(screen.getByText('INV/26-27/000007')).toBeInTheDocument();
    expect(screen.getByText('15/11/2026, 12:00 pm')).toBeInTheDocument();
    expect(screen.getByText('Paneer Tikka')).toBeInTheDocument();
    expect(screen.getByText('440.00')).toBeInTheDocument();
    expect(screen.getByText('CGST 2.5%')).toBeInTheDocument();
    expect(screen.getByText('SGST 9%')).toBeInTheDocument();
    expect(screen.getAllByText('5.40')).toHaveLength(2);
    expect(screen.getByText('0.20')).toBeInTheDocument();
    expect(screen.getByText('₹533.00')).toBeInTheDocument();
    expect(screen.getByText('Follow us @anandthali')).toBeInTheDocument();
    expect(screen.queryByText('DUPLICATE')).not.toBeInTheDocument();
    expect(screen.getByTestId('bill-receipt')).toHaveClass('receipt-80');
  });

  it('shows the UPI QR and amount due only for an unpaid bill', () => {
    render(<BillReceipt bill={GST_BILL} qrUrl="/api/bills/7/upi-qr.svg" />);
    expect(screen.getByAltText('Scan to pay with any UPI app')).toHaveAttribute('src', '/api/bills/7/upi-qr.svg');
    expect(screen.getByText('Due')).toBeInTheDocument();
  });

  it('shows payments, change and PAID; no QR', () => {
    const paid = { ...GST_BILL, status: 'Paid', amountDue: 0, payments: [{ id: 1, method: 'Cash', amount: 533, tendered: 600, change: 67 }] };
    render(<BillReceipt bill={paid} qrUrl="/api/bills/7/upi-qr.svg" duplicate />);
    expect(screen.getByText('Cash')).toBeInTheDocument();
    expect(screen.getByText('600.00')).toBeInTheDocument();
    expect(screen.getByText('67.00')).toBeInTheDocument();
    expect(screen.getByText('PAID')).toBeInTheDocument();
    expect(screen.getByText('DUPLICATE')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('prints a composition bill as a bill of supply with the legal line and no tax', () => {
    const composition = { ...GST_BILL, taxMode: 'Composition', taxNote: 'Composition taxable person, not eligible to collect tax on supplies',
      taxableValue: 450, cgst: 0, sgst: 0, roundOff: 0, discount: 50, total: 450, taxSummary: [] };
    render(<BillReceipt bill={composition} width={58} />);
    expect(screen.getByText('BILL OF SUPPLY')).toBeInTheDocument();
    expect(screen.getByText('Composition taxable person, not eligible to collect tax on supplies')).toBeInTheDocument();
    expect(screen.getByText('-50.00')).toBeInTheDocument();
    expect(screen.queryByText(/CGST/)).not.toBeInTheDocument();
    expect(screen.queryByText('Taxable value')).not.toBeInTheDocument();
    expect(screen.getByTestId('bill-receipt')).toHaveClass('receipt-58');
  });

  it('prints in Gujarati when the outlet chooses it, and marks a void bill', () => {
    render(<BillReceipt bill={{ ...GST_BILL, status: 'Void' }} lang="gu" />);
    expect(screen.getByText('ટેક્સ ઇન્વૉઇસ')).toBeInTheDocument();
    expect(screen.getByText('કુલ')).toBeInTheDocument();
    expect(screen.getByText('રદ')).toBeInTheDocument();
  });

  it('formats India time and half rates', () => {
    expect(billDateTime('2026-11-14T20:15:00')).toBe('15/11/2026, 01:45 am');
    expect(halfRate(5)).toBe('2.5');
    expect(halfRate(18)).toBe('9');
  });
});

describe('Kitchen ticket', () => {
  it('shows token, table, items with notes and no prices', () => {
    const order = { id: 3, tokenNumber: 12, orderType: 'DineIn', table: { id: 1, tableNumber: '5' }, createdAt: '2026-11-15T06:30:00Z',
      items: [{ id: 1, itemName: 'Paneer Tikka', quantity: 2, unitPrice: 220, note: 'Jain' }, { id: 2, itemName: 'Cola', quantity: 1, unitPrice: 60, note: null }] };
    render(<KotTicket order={order} />);
    expect(screen.getByText('#12')).toBeInTheDocument();
    expect(screen.getByText('Table 5')).toBeInTheDocument();
    expect(screen.getByText('x 2')).toBeInTheDocument();
    expect(screen.getByText('Note: Jain')).toBeInTheDocument();
    expect(screen.getByText('3 items')).toBeInTheDocument();
    expect(screen.queryByText(/220/)).not.toBeInTheDocument();
  });

  it('prints only the new round of a running tab', () => {
    const order = { id: 6, tokenNumber: 15, orderType: 'DineIn', table: { id: 1, tableNumber: '4' }, createdAt: '2026-11-15T06:30:00Z',
      items: [{ id: 1, itemName: 'Butter Naan', quantity: 2, round: 1 }, { id: 2, itemName: 'Lassi', quantity: 1, round: 2 }] };
    render(<KotTicket order={order} round={2} />);
    expect(screen.getByText('Round 2 · added items')).toBeInTheDocument();
    expect(screen.getByText('Lassi')).toBeInTheDocument();
    expect(screen.queryByText('Butter Naan')).not.toBeInTheDocument();
  });

  it('prints one ticket per kitchen station', () => {
    const order = { id: 5, tokenNumber: 14, orderType: 'DineIn', table: { id: 1, tableNumber: '2' }, createdAt: '2026-11-15T06:30:00Z',
      items: [
        { id: 1, itemName: 'Butter Naan', quantity: 4, stationId: 3, stationName: 'Tandoor' },
        { id: 2, itemName: 'Dal Fry', quantity: 1 },
        { id: 3, itemName: 'Lassi', quantity: 2, stationId: 4, stationName: 'Bar' },
      ] };
    render(<KotTicket order={order} />);
    const tickets = screen.getAllByTestId('kot-ticket');
    expect(tickets).toHaveLength(3);
    expect(tickets[0]).toHaveTextContent('Main kitchen');
    expect(tickets[0]).toHaveTextContent('Dal Fry');
    expect(tickets[1]).toHaveTextContent('Tandoor');
    expect(tickets[1]).toHaveTextContent('4 items');
    expect(tickets[1]).not.toHaveTextContent('Dal Fry');
    expect(tickets[2]).toHaveTextContent('Lassi');
  });

  it('lists what a combo includes', () => {
    const order = { id: 7, tokenNumber: 16, orderType: 'Takeaway', customerName: 'Asha', table: null, createdAt: '2026-11-15T06:30:00Z',
      items: [{ id: 1, itemName: 'Burger Meal', quantity: 1, includes: '1 x Burger, 1 x Fries, 1 x Cola' }] };
    render(<KotTicket order={order} />);
    expect(screen.getByText('= 1 x Burger, 1 x Fries, 1 x Cola')).toBeInTheDocument();
  });

  it('shows the customer for takeaway', () => {
    render(<KotTicket order={{ id: 4, tokenNumber: 13, orderType: 'Takeaway', customerName: 'Ravi', table: null, createdAt: '2026-11-15T06:30:00Z', items: [] }} lang="gu" />);
    expect(screen.getByText('ટેકઅવે')).toBeInTheDocument();
    expect(screen.getByText('Ravi')).toBeInTheDocument();
  });
});
