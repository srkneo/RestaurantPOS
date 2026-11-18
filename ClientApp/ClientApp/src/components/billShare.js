import { formatMoney } from '../ui/format';

// Indian mobile number for wa.me (country code, digits only), or null when the bill has none.
export function whatsAppNumber(phone) {
  const digits = String(phone ?? '').replace(/\D/g, '');
  if (digits.length === 10) return `91${digits}`;
  if (digits.length === 12 && digits.startsWith('91')) return digits;
  if (digits.length === 11 && digits.startsWith('0')) return `91${digits.slice(1)}`;
  return null;
}

// The bill as a short WhatsApp message. With the customer's phone it opens their chat; otherwise the cashier picks the contact.
export function billWhatsAppUrl(bill, t) {
  const store = bill.store ?? {};
  const lines = [t('share.billTitle', { restaurant: store.restaurantName ?? '', number: bill.invoiceNumber })];
  (bill.lines ?? []).forEach((line) => {
    lines.push(t('share.billLine', { qty: line.quantity, item: line.itemName, amount: formatMoney(line.amount) }));
  });
  if (Number(bill.discount) > 0) lines.push(t('share.billDiscount', { amount: formatMoney(bill.discount) }));
  lines.push(t('share.billTotal', { total: formatMoney(bill.total) }));
  if (bill.status === 'Paid') lines.push(t('share.billPaid'));
  else if (Number(bill.amountDue) > 0) lines.push(t('share.billDue', { amount: formatMoney(bill.amountDue) }));
  if (store.footer) lines.push(store.footer);
  const number = whatsAppNumber(bill.customerPhone);
  return `https://wa.me/${number ?? ''}?text=${encodeURIComponent(lines.join('\n'))}`;
}
