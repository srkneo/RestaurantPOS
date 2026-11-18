import React from 'react';
import { printT, money, billDateTime, halfRate } from './printText';
import './print.css';

// The printed bill. Everything comes from the saved bill, so a reprint always matches the original.
export default function BillReceipt({ bill, lang = 'en', width = 80, duplicate = false, qrUrl = null, reviewQrUrl = null, feedbackQrUrl = null }) {
  const t = printT(lang);
  const store = bill.store ?? {};
  const regular = bill.taxMode === 'Regular';
  const payments = bill.payments ?? [];

  return (
    <div className={`receipt receipt-${width}`} data-testid="bill-receipt">
      <div className="receipt-center">
        <div className="receipt-big">{store.restaurantName}</div>
        {store.outletName && store.outletName !== store.restaurantName && <div>{store.outletName}</div>}
        {store.address && <div>{store.address}</div>}
        {store.phone && <div>{t('print.phone')}: {store.phone}</div>}
        {store.gstin && <div>{t('print.gstin')}: {store.gstin}</div>}
        {store.fssai && <div>{t('print.fssai')}: {store.fssai}</div>}
        <div className="receipt-title">{t(`print.title.${bill.taxMode}`)}</div>
        {duplicate && <div className="receipt-flag">{t('print.duplicate')}</div>}
        {bill.status === 'Void' && <div className="receipt-flag">{t('print.void')}</div>}
      </div>

      <div className="receipt-line" />
      <div className="receipt-row"><span>{t('print.billNo')}</span><span>{bill.invoiceNumber}</span></div>
      <div className="receipt-row"><span>{t('print.date')}</span><span>{billDateTime(bill.issuedAt)}</span></div>
      <div className="receipt-row"><span>{bill.orderLabel}</span><span>{t('print.token')} {bill.tokenNumber}</span></div>
      {bill.customerName && <div className="receipt-row"><span>{t('print.customer')}</span><span>{bill.customerName}</span></div>}

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
      <div className="receipt-row"><span>{t('print.subtotal')}</span><span>{money(bill.itemsTotal)}</span></div>
      {Number(bill.discount) > 0 && <div className="receipt-row"><span>{t('print.discount')}</span><span>-{money(bill.discount)}</span></div>}
      {regular && <div className="receipt-row"><span>{t('print.taxable')}</span><span>{money(bill.taxableValue)}</span></div>}
      {regular && (bill.taxSummary ?? []).filter((row) => Number(row.taxRate) > 0).map((row) => (
        <React.Fragment key={row.taxRate}>
          <div className="receipt-row"><span>{t('print.cgst', { rate: halfRate(row.taxRate) })}</span><span>{money(row.cgst)}</span></div>
          <div className="receipt-row"><span>{t('print.sgst', { rate: halfRate(row.taxRate) })}</span><span>{money(row.sgst)}</span></div>
        </React.Fragment>
      ))}
      {Number(bill.roundOff) !== 0 && <div className="receipt-row"><span>{t('print.roundOff')}</span><span>{money(bill.roundOff)}</span></div>}
      <div className="receipt-row receipt-total"><span>{t('print.total')}</span><span>₹{money(bill.total)}</span></div>

      {payments.length > 0 && <div className="receipt-line" />}
      {payments.map((p) => (
        <React.Fragment key={p.id}>
          <div className="receipt-row"><span>{t(`print.method.${p.method}`)}</span><span>{money(p.amount)}</span></div>
          {p.tendered != null && <div className="receipt-row receipt-note"><span>{t('print.tendered')}</span><span>{money(p.tendered)}</span></div>}
          {p.change != null && Number(p.change) > 0 && <div className="receipt-row receipt-note"><span>{t('print.change')}</span><span>{money(p.change)}</span></div>}
        </React.Fragment>
      ))}
      {bill.status === 'Paid' && <div className="receipt-center receipt-title">{t('print.paid')}</div>}
      {bill.status === 'Unpaid' && Number(bill.amountDue) > 0 && (
        <>
          <div className="receipt-row"><span>{t('print.due')}</span><span>{money(bill.amountDue)}</span></div>
          {qrUrl && (
            <div className="receipt-center">
              <img className="receipt-qr" src={qrUrl} alt={t('print.scanToPay')} />
              <div className="receipt-note">{t('print.scanToPay')}</div>
            </div>
          )}
        </>
      )}

      {bill.taxNote && <div className="receipt-center receipt-note" style={{ marginTop: '2mm' }}>{bill.taxNote}</div>}
      <div className="receipt-line" />
      {store.footer && <div className="receipt-center">{store.footer}</div>}
      {feedbackQrUrl && (
        <div className="receipt-center">
          <img className="receipt-qr" src={feedbackQrUrl} alt={t('print.feedback')} />
          <div className="receipt-note">{t('print.feedback')}</div>
        </div>
      )}
      {reviewQrUrl && (
        <div className="receipt-center">
          <img className="receipt-qr" src={reviewQrUrl} alt={t('print.reviewUs')} />
          <div className="receipt-note">{t('print.reviewUs')}</div>
        </div>
      )}
      <div className="receipt-center">{t('print.thanks')}</div>
    </div>
  );
}
