import React from 'react';
import { printT, money, billDateTime } from './printText';
import './print.css';

export default function CreditNoteReceipt({ note, lang = 'en', width = 80 }) {
  const t = printT(lang);
  const bill = note.bill;
  const store = bill.store ?? {};

  return (
    <div className={`receipt receipt-${width}`} data-testid="credit-note-receipt">
      <div className="receipt-center">
        <div className="receipt-big">{store.restaurantName}</div>
        {store.outletName && store.outletName !== store.restaurantName && <div>{store.outletName}</div>}
        {store.address && <div>{store.address}</div>}
        {store.gstin && <div>{t('print.gstin')}: {store.gstin}</div>}
        <div className="receipt-title">{t('print.creditNote')}</div>
      </div>

      <div className="receipt-line" />
      <div className="receipt-row"><span>{t('print.creditNoteNo')}</span><span>{note.number}</span></div>
      <div className="receipt-row"><span>{t('print.date')}</span><span>{billDateTime(note.issuedAt)}</span></div>
      <div className="receipt-row"><span>{t('print.againstBill')}</span><span>{bill.invoiceNumber}</span></div>
      <div className="receipt-row"><span>{t('print.date')}</span><span>{billDateTime(bill.issuedAt)}</span></div>
      <div className="receipt-row"><span>{t('print.reason')}</span><span>{note.reason}</span></div>

      <div className="receipt-line" />
      <table>
        <thead>
          <tr><th>{t('print.item')}</th><th className="num">{t('print.qty')}</th><th className="num">{t('print.rate')}</th><th className="num">{t('print.amount')}</th></tr>
        </thead>
        <tbody>
          {bill.lines.map((line, i) => (
            <tr key={i}>
              <td>{line.itemName}</td>
              <td className="num">{line.quantity}</td>
              <td className="num">{money(line.unitPrice)}</td>
              <td className="num">{money(line.amount)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="receipt-line" />
      {Number(note.cgst) > 0 && (
        <>
          <div className="receipt-row"><span>{t('print.taxable')}</span><span>{money(note.taxableValue)}</span></div>
          <div className="receipt-row"><span>{t('print.cgstTotal')}</span><span>{money(note.cgst)}</span></div>
          <div className="receipt-row"><span>{t('print.sgstTotal')}</span><span>{money(note.sgst)}</span></div>
        </>
      )}
      {Number(note.roundOff) !== 0 && <div className="receipt-row"><span>{t('print.roundOff')}</span><span>{money(note.roundOff)}</span></div>}
      <div className="receipt-row receipt-total"><span>{t('print.total')}</span><span>₹{money(note.total)}</span></div>

      {note.refundMethod && (
        <div className="receipt-row"><span>{`${t('print.refund')} (${t(`print.method.${note.refundMethod}`)})`}</span><span>{money(note.refundAmount)}</span></div>
      )}
      {note.approvedBy && (
        <>
          <div className="receipt-row"><span>{t('print.approvedBy')}</span><span>{note.approvedBy}</span></div>
          <div className="receipt-line" />
        </>
      )}
    </div>
  );
}
