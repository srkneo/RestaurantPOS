import { describe, it, expect } from 'vitest';
import { billWhatsAppUrl, whatsAppNumber } from './billShare';

const t = (key, vars = {}) => `${key}:${Object.values(vars).join('|')}`;

describe('Bill on WhatsApp', () => {
  it('turns Indian phone numbers into wa.me numbers', () => {
    expect(whatsAppNumber('98765 43210')).toBe('919876543210');
    expect(whatsAppNumber('+91 98765-43210')).toBe('919876543210');
    expect(whatsAppNumber('09876543210')).toBe('919876543210');
    expect(whatsAppNumber('12345')).toBeNull();
    expect(whatsAppNumber(null)).toBeNull();
  });

  it('opens the customer chat when the bill has a phone, otherwise lets the cashier pick', () => {
    const bill = { invoiceNumber: 'INV/1', total: 100, amountDue: 0, status: 'Paid', store: { restaurantName: 'Cafe' }, lines: [], customerPhone: '9876543210' };
    expect(billWhatsAppUrl(bill, t).startsWith('https://wa.me/919876543210?text=')).toBe(true);
    expect(billWhatsAppUrl({ ...bill, customerPhone: null }, t).startsWith('https://wa.me/?text=')).toBe(true);
  });

  it('lists the lines, discount, total and what is due', () => {
    const bill = {
      invoiceNumber: 'INV/2', total: 450, discount: 50, amountDue: 450, status: 'Unpaid', store: { restaurantName: 'Cafe', footer: 'Visit again' },
      lines: [{ itemName: 'Thali', quantity: 2, amount: 500 }],
    };
    const text = decodeURIComponent(billWhatsAppUrl(bill, t).split('?text=')[1]).split('\n');
    expect(text).toEqual([
      'share.billTitle:Cafe|INV/2', 'share.billLine:2|Thali|₹500.00', 'share.billDiscount:₹50.00', 'share.billTotal:₹450.00',
      'share.billDue:₹450.00', 'Visit again',
    ]);
  });
});
