// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import CreditNoteReceipt from './CreditNoteReceipt';

const NOTE = {
  id: 3, number: 'CN/26-27/0000001', issuedAt: '2026-11-16T06:00:00Z', reason: 'Food was cold',
  taxableValue: 500, cgst: 16.4, sgst: 16.4, roundOff: 0.2, total: 533, refundMethod: 'Cash', refundAmount: 533, approvedBy: 'Meena',
  bill: {
    id: 7, invoiceNumber: 'INV/26-27/000007', issuedAt: '2026-11-15T06:00:00Z', taxMode: 'Regular',
    store: { restaurantName: 'Anand Thali', outletName: 'Anand Thali', gstin: '24ABCDE1234F1Z5' },
    lines: [{ itemName: 'Paneer Tikka', quantity: 2, unitPrice: 220, amount: 440 }, { itemName: 'Cola', quantity: 1, unitPrice: 60, amount: 60 }],
  },
};

afterEach(cleanup);

describe('Credit note receipt', () => {
  it('prints the credit note against its bill', () => {
    render(<CreditNoteReceipt note={NOTE} />);
    expect(screen.getByText('CREDIT NOTE')).toBeInTheDocument();
    expect(screen.getByText('CN/26-27/0000001')).toBeInTheDocument();
    expect(screen.getByText('INV/26-27/000007')).toBeInTheDocument();
    expect(screen.getByText('Food was cold')).toBeInTheDocument();
    expect(screen.getByText('Paneer Tikka')).toBeInTheDocument();
    expect(screen.getByText('₹533.00')).toBeInTheDocument();
    expect(screen.getByText('Meena')).toBeInTheDocument();
    expect(screen.getByText('24ABCDE1234F1Z5', { exact: false })).toBeInTheDocument();
  });
});
