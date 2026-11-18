import React from 'react';
import { printT, money, billDateTime, halfRate } from './printText';
import './print.css';

function Row({ label, value, strong }) {
  return <div className={`receipt-row${strong ? ' receipt-total' : ''}`}><span>{label}</span><span>{value}</span></div>;
}

// End-of-day (Z) report on receipt paper: sales, GST per rate, money by method, void bills, items and till shifts.
export default function DayReport({ report, lang = 'en', width = 80 }) {
  const t = printT(lang);
  const r = report;
  return (
    <div className={`receipt receipt-${width}`} data-testid="day-report">
      <div className="receipt-center">
        <div className="receipt-big">{r.restaurantName}</div>
        {r.outletName && r.outletName !== r.restaurantName && <div>{r.outletName}</div>}
        {r.gstin && <div>{t('print.gstin')}: {r.gstin}</div>}
        <div className="receipt-title">{t('print.zReport')}</div>
        <div>{t('print.businessDate')}: {r.businessDate}</div>
        <div className="receipt-note">{t('print.printedAt')}: {billDateTime(r.generatedAt)}</div>
      </div>

      <div className="receipt-line" />
      <Row label={t('print.bills')} value={r.billCount} />
      {r.firstInvoice && <div className="receipt-note">{r.firstInvoice} – {r.lastInvoice}</div>}
      <Row label={t('print.subtotal')} value={money(r.itemsTotal)} />
      {Number(r.discount) > 0 && <Row label={t('print.discount')} value={`-${money(r.discount)}`} />}
      {(Number(r.cgst) > 0 || Number(r.sgst) > 0) && (
        <>
          <Row label={t('print.taxable')} value={money(r.taxableValue)} />
          <Row label={t('print.cgstTotal')} value={money(r.cgst)} />
          <Row label={t('print.sgstTotal')} value={money(r.sgst)} />
        </>
      )}
      {Number(r.roundOff) !== 0 && <Row label={t('print.roundOff')} value={money(r.roundOff)} />}
      <Row label={t('print.sales')} value={money(r.total)} strong />

      {r.taxSummary.length > 0 && (
        <>
          <div className="receipt-line" />
          <div className="receipt-title">{t('print.gstByRate')}</div>
          <table>
            <thead><tr><th>{t('print.gstRate')}</th><th className="num">{t('print.taxable')}</th><th className="num">CGST</th><th className="num">SGST</th></tr></thead>
            <tbody>
              {r.taxSummary.map((row) => (
                <tr key={row.taxRate}>
                  <td>{Number(row.taxRate)}% ({halfRate(row.taxRate)}+{halfRate(row.taxRate)})</td>
                  <td className="num">{money(row.taxableValue)}</td><td className="num">{money(row.cgst)}</td><td className="num">{money(row.sgst)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      <div className="receipt-line" />
      <div className="receipt-title">{t('print.payments')}</div>
      {r.payments.map((p) => <Row key={p.method} label={`${t(`print.method.${p.method}`)} (${p.count})`} value={money(p.amount)} />)}
      <Row label={t('print.received')} value={money(r.amountReceived)} strong />
      {r.unpaidCount > 0 && <Row label={`${t('print.unpaid')} (${r.unpaidCount})`} value={money(r.amountDue)} />}

      {r.voidCount > 0 && (
        <>
          <div className="receipt-line" />
          <Row label={`${t('print.voidBills')} (${r.voidCount})`} value={money(r.voidTotal)} />
          {r.voidedBills.map((b) => <div key={b.id} className="receipt-note">{b.invoiceNumber} {money(b.total)} – {b.reason}</div>)}
        </>
      )}

      {(r.creditNotes ?? []).length > 0 && (
        <>
          <div className="receipt-line" />
          <Row label={`${t('print.creditNotes')} (${r.creditNotes.length})`} value={`-${money(r.creditTotal)}`} />
          {r.creditNotes.map((c) => <div key={c.id} className="receipt-note">{c.number} ({c.billNumber}) – {c.reason}</div>)}
          <Row label={t('print.netSales')} value={money(r.netSales)} strong />
        </>
      )}

      {(Number(r.tipTotal ?? 0) > 0 || Number(r.expenseTotal ?? 0) > 0) && (
        <>
          <div className="receipt-line" />
          {Number(r.tipTotal ?? 0) > 0 && <Row label={t('print.tips')} value={money(r.tipTotal)} />}
          {Number(r.expenseTotal ?? 0) > 0 && <Row label={t('print.expenses')} value={money(r.expenseTotal)} />}
          {Number(r.expenseFromTill ?? 0) > 0 && <Row label={t('print.expensesFromTill')} value={money(r.expenseFromTill)} />}
        </>
      )}

      {r.items.length > 0 && (
        <>
          <div className="receipt-line" />
          <div className="receipt-title">{t('print.itemSales')}</div>
          {r.items.map((i) => <Row key={i.itemName} label={`${i.itemName} x ${i.quantity}`} value={money(i.amount)} />)}
        </>
      )}

      {r.shifts.map((s, n) => (
        <div key={s.id}>
          <div className="receipt-line" />
          <div className="receipt-title">{t('print.shift', { number: n + 1 })}</div>
          <div className="receipt-note">{billDateTime(s.openedAt)}{s.closedAt ? ` – ${billDateTime(s.closedAt)}` : ` – ${t('print.shiftOpen')}`}</div>
          <Row label={t('print.openingCash')} value={money(s.openingCash)} />
          <Row label={t('print.cashSales')} value={money(s.cashSales)} />
          {Number(s.cashIn) > 0 && <Row label={t('print.cashIn')} value={money(s.cashIn)} />}
          {Number(s.cashOut) > 0 && <Row label={t('print.cashOut')} value={`-${money(s.cashOut)}`} />}
          <Row label={t('print.expectedCash')} value={money(s.expectedCash)} />
          {s.countedCash !== null && s.countedCash !== undefined && (
            <>
              <Row label={t('print.countedCash')} value={money(s.countedCash)} />
              <Row label={Number(s.difference) < 0 ? t('print.short') : Number(s.difference) > 0 ? t('print.excess') : t('print.matched')}
                value={money(Math.abs(Number(s.difference)))} strong />
            </>
          )}
        </div>
      ))}
      <div className="receipt-line" />
    </div>
  );
}
