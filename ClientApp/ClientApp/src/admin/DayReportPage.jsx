import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import DayReport from '../print/DayReport';
import { formatMoney } from '../ui/format';

const qty = (n) => Number(n ?? 0).toLocaleString('en-IN', { maximumFractionDigits: 3 });
import '../components/OrderHistory.css';
import '../components/DayClosePage.css';

// Owner's end-of-day (Z) report for one business day, shown as it prints.
export default function DayReportPage() {
  const t = useT();
  const [date, setDate] = useState('');
  const [state, setState] = useState({ status: 'loading' });

  const load = useCallback(async (day) => {
    setState({ status: 'loading' });
    try {
      const query = day ? `?date=${encodeURIComponent(day)}` : '';
      const [reportRes, settingsRes, tipsRes, stockRes] = await Promise.all([
        fetch(`/api/reports/day${query}`), fetch('/api/print-settings'), fetch(`/api/tips${query}`).catch(() => null),
        fetch('/api/stock').catch(() => null)]);
      if (!reportRes.ok) {
        setState({ status: 'error' });
        return;
      }
      const report = await reportRes.json();
      const settings = settingsRes.ok ? await settingsRes.json() : { printLanguage: 'en', paperWidth: 80 };
      // Tips are kept apart from sales; the report still shows when they cannot be loaded.
      const tips = tipsRes?.ok ? await tipsRes.json() : null;
      // Stock as it is now (not for the chosen day): only the items at or under their low level.
      const stock = stockRes?.ok ? await stockRes.json().catch(() => []) : [];
      const lowStock = Array.isArray(stock) ? stock.filter((s) => s.isLow) : [];
      setState({ status: 'ready', report, settings, tips, lowStock });
      setDate(report.businessDate);
    } catch {
      setState({ status: 'error' });
    }
  }, []);

  useEffect(() => { load(''); }, [load]);

  const print = () => window.open(`/print/day?date=${state.report.businessDate}&auto=1`, '_blank', 'noopener');

  // The day in a few lines for the owner or a partner, opened in WhatsApp (they choose who to send it to).
  const share = () => {
    const r = state.report;
    const paid = (method) => formatMoney(r.payments?.find((p) => p.method === method)?.amount ?? 0);
    const lines = [
      t('share.title', { restaurant: r.restaurantName, outlet: r.outletName, date: r.businessDate }),
      t('share.sales', { count: r.billCount, total: formatMoney(r.total) }),
      t('share.payments', { cash: paid('Cash'), upi: paid('Upi'), card: paid('Card') }),
    ];
    if (Number(r.unpaidCount) > 0) lines.push(t('share.unpaid', { count: r.unpaidCount, amount: formatMoney(r.amountDue) }));
    if (Number(r.expenseTotal ?? 0) > 0 || Number(r.tipTotal ?? 0) > 0) {
      lines.push(t('share.extra', { expenses: formatMoney(r.expenseTotal ?? 0), tips: formatMoney(r.tipTotal ?? 0) }));
    }
    if (Number(r.feedbackCount ?? 0) > 0) {
      lines.push(t('share.feedback', { average: Number(r.feedbackAverage).toFixed(1), count: r.feedbackCount }));
    }
    lines.push(t('share.net', { net: formatMoney(r.netSales) }));
    if ((state.lowStock?.length ?? 0) > 0) {
      lines.push(t('share.lowStock', { items: state.lowStock.map((s) => `${s.name} ${qty(s.quantity)} ${s.unit}`).join(', ') }));
    }
    window.open(`https://wa.me/?text=${encodeURIComponent(lines.join('\n'))}`, '_blank', 'noopener');
  };

  return (
    <main className="pos-page">
      <div className="pos-page-head"><h1>{t('dayReport.title')}</h1></div>
      <form className="day-report-bar" onSubmit={(e) => { e.preventDefault(); load(date); }}>
        <label htmlFor="report-date" className="fw-semibold">{t('dayReport.date')}</label>
        <input id="report-date" type="date" className="form-control" value={date} onChange={(e) => setDate(e.target.value)} />
        <button type="submit" className="btn btn-outline-secondary">{t('dayReport.show')}</button>
        {state.status === 'ready' && <button type="button" className="btn btn-primary px-4" onClick={print}>{t('dayReport.print')}</button>}
        {state.status === 'ready' && <button type="button" className="btn btn-outline-success px-4" onClick={share}>{t('share.button')}</button>}
      </form>
      {state.status === 'loading' && <StateMessage kind="loading" />}
      {state.status === 'error' && <StateMessage kind="error" onRetry={() => load(date)} />}
      {state.status === 'ready' && (
        <div className="day-report-sheet">
          <DayReport report={state.report} lang={state.settings.printLanguage} width={state.settings.paperWidth} />
        </div>
      )}
      {state.status === 'ready' && (state.tips?.waiters?.length ?? 0) > 0 && (
        <section className="pos-card p-3 mt-3" aria-labelledby="tips-title">
          <h2 id="tips-title" className="h5">{t('tips.title')}</h2>
          <div className="table-responsive">
            <table className="table align-middle mb-2">
              <thead>
                <tr>
                  <th scope="col">{t('tips.waiter')}</th>
                  <th scope="col" className="text-end">{t('tips.count')}</th>
                  <th scope="col" className="text-end">{t('tips.cash')}</th>
                  <th scope="col" className="text-end">{t('tips.total')}</th>
                </tr>
              </thead>
              <tbody>
                {state.tips.waiters.map((w) => (
                  <tr key={w.waiterName}>
                    <td>{w.waiterName === 'Shared' ? t('tips.shared') : w.waiterName}</td>
                    <td className="text-end">{w.count}</td>
                    <td className="text-end">{formatMoney(w.cash)}</td>
                    <td className="text-end fw-semibold">{formatMoney(w.amount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="pos-hint mb-0">{t('tips.hint')}</p>
        </section>
      )}
      {state.status === 'ready' && (state.lowStock?.length ?? 0) > 0 && (
        <section className="pos-card p-3 mt-3" aria-labelledby="low-stock-title">
          <h2 id="low-stock-title" className="h5">{t('dayReport.lowStock')}</h2>
          <ul className="mb-2">
            {state.lowStock.map((s) => (
              <li key={s.id}><span className="fw-semibold">{s.name}</span>: <span>{t('dayReport.lowStockLine', { quantity: qty(s.quantity), unit: s.unit, low: qty(s.lowLevel) })}</span></li>
            ))}
          </ul>
          <a href="/stock">{t('dayReport.openStock')}</a>
        </section>
      )}
    </main>
  );
}
