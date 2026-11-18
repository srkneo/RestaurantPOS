import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import { formatMoney } from '../ui/format';
import '../components/OrderHistory.css';

// yyyy-MM-dd of a local date.
const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

function quickRange(kind) {
  const today = new Date();
  if (kind === 'week') {
    const start = new Date(today);
    start.setDate(today.getDate() - 6);
    return { from: iso(start), to: iso(today) };
  }
  if (kind === 'month') return { from: iso(new Date(today.getFullYear(), today.getMonth(), 1)), to: iso(today) };
  return { from: iso(today), to: iso(today) };
}

function Table({ title, head, rows, empty }) {
  return (
    <section className="pos-card p-3" aria-label={title}>
      <h2 className="h5">{title}</h2>
      {rows.length === 0 ? <p className="pos-hint mb-0">{empty}</p> : (
        <div className="table-responsive">
          <table className="table align-middle mb-0">
            <thead>
              <tr>{head.map((h, i) => <th key={h} scope="col" className={i === 0 ? '' : 'text-end'}>{h}</th>)}</tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.key}>{r.cells.map((c, i) => <td key={i} className={i === 0 ? '' : 'text-end'}>{c}</td>)}</tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

// Owner and manager: sales for a range of days, and the GST bill list as CSV for the accountant.
export default function ReportsPage() {
  const t = useT();
  const [range, setRange] = useState(() => quickRange('today'));
  const [state, setState] = useState({ status: 'loading' });

  const load = useCallback(async (r) => {
    setState({ status: 'loading' });
    try {
      const res = await fetch(`/api/reports/sales?from=${r.from}&to=${r.to}`);
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setState({ status: 'error', message: data.message });
        return;
      }
      setState({ status: 'ready', report: data });
    } catch {
      setState({ status: 'error' });
    }
  }, []);

  useEffect(() => { load(quickRange('today')); }, [load]);

  const pick = (kind) => {
    const r = quickRange(kind);
    setRange(r);
    load(r);
  };

  const report = state.report;
  const n = (v) => formatMoney(v);
  return (
    <main className="pos-page">
      <div className="pos-page-head"><h1>{t('reports.title')}</h1></div>
      <form className="d-flex flex-wrap align-items-end gap-2 mb-3" onSubmit={(e) => { e.preventDefault(); load(range); }}>
        <div>
          <label htmlFor="reports-from" className="form-label">{t('reports.from')}</label>
          <input id="reports-from" type="date" className="form-control" value={range.from} onChange={(e) => setRange({ ...range, from: e.target.value })} />
        </div>
        <div>
          <label htmlFor="reports-to" className="form-label">{t('reports.to')}</label>
          <input id="reports-to" type="date" className="form-control" value={range.to} onChange={(e) => setRange({ ...range, to: e.target.value })} />
        </div>
        <button type="submit" className="btn btn-primary px-4">{t('reports.show')}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={() => pick('today')}>{t('reports.today')}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={() => pick('week')}>{t('reports.week')}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={() => pick('month')}>{t('reports.month')}</button>
        <a className="btn btn-outline-primary ms-auto" href={`/api/reports/sales.csv?from=${range.from}&to=${range.to}`} download>{t('reports.salesCsv')}</a>
        <a className="btn btn-outline-primary" href={`/api/reports/gst.csv?from=${range.from}&to=${range.to}`} download>{t('reports.csv')}</a>
        <a className="btn btn-outline-primary" href={`/api/reports/tally.xml?from=${range.from}&to=${range.to}`} download title={t('reports.tallyHint')}>{t('reports.tally')}</a>
      </form>

      {state.status === 'loading' && <StateMessage kind="loading" />}
      {state.status === 'error' && (state.message
        ? <p className="text-danger" role="alert">{state.message}</p>
        : <StateMessage kind="error" onRetry={() => load(range)} />)}
      {state.status === 'ready' && (
        <div className="d-flex flex-column gap-3">
          <section className="pos-card p-3" aria-label={t('reports.summary')}>
            <div className="d-flex flex-wrap gap-4">
              <div><div className="pos-hint">{t('reports.bills')}</div><div className="h4 mb-0">{report.billCount}</div></div>
              <div><div className="pos-hint">{t('reports.total')}</div><div className="h4 mb-0">{n(report.total)}</div></div>
              <div><div className="pos-hint">{t('reports.net')}</div><div className="h4 mb-0">{n(report.netSales)}</div></div>
              <div><div className="pos-hint">{t('reports.discount')}</div><div className="h4 mb-0">{n(report.discount)}</div></div>
              <div><div className="pos-hint">{t('reports.gst')}</div><div className="h4 mb-0">{n(Number(report.cgst) + Number(report.sgst))}</div></div>
              <div><div className="pos-hint">{t('reports.expenses')}</div><div className="h4 mb-0">{n(report.expenseTotal ?? 0)}</div></div>
            </div>
            <p className="pos-hint mb-0 mt-2">{t('reports.hint')}</p>
          </section>
          <Table title={t('reports.byDay')} head={[t('reports.date'), t('reports.bills'), t('reports.total')]} empty={t('reports.empty')}
            rows={report.days.map((d) => ({ key: d.date, cells: [d.date, d.bills, n(d.total)] }))} />
          <Table title={t('reports.byCategory')} head={[t('reports.category'), t('reports.qty'), t('reports.amount')]} empty={t('reports.empty')}
            rows={report.categories.map((c) => ({ key: c.category, cells: [c.category, c.quantity, n(c.amount)] }))} />
          <Table title={t('reports.topItems')} head={[t('reports.item'), t('reports.qty'), t('reports.amount')]} empty={t('reports.empty')}
            rows={report.items.slice(0, 20).map((i) => ({ key: i.itemName, cells: [i.itemName, i.quantity, n(i.amount)] }))} />
          <Table title={t('reports.payments')} head={[t('reports.method'), t('reports.count'), t('reports.amount')]} empty={t('reports.empty')}
            rows={report.payments.map((p) => ({ key: p.method, cells: [t(`bill.method.${p.method}`), p.count, n(p.amount)] }))} />
          <Table title={t('reports.byHour')} head={[t('reports.hour'), t('reports.bills'), t('reports.total')]} empty={t('reports.empty')}
            rows={report.hours.map((h) => ({ key: h.hour, cells: [`${String(h.hour).padStart(2, '0')}:00`, h.bills, n(h.total)] }))} />
          <Table title={t('reports.byStaff')} head={[t('reports.staff'), t('reports.bills'), t('reports.total')]} empty={t('reports.empty')}
            rows={report.staff.map((s) => ({ key: s.name ?? '-', cells: [s.name ?? t('reports.notRecorded'), s.bills, n(s.total)] }))} />
          <Table title={t('reports.taxByRate')} head={[t('reports.rate'), t('reports.taxable'), t('reports.cgst'), t('reports.sgst')]} empty={t('reports.noGst')}
            rows={report.taxSummary.map((r) => ({ key: r.taxRate, cells: [`${r.taxRate}%`, n(r.taxableValue), n(r.cgst), n(r.sgst)] }))} />
          <Table title={t('reports.byExpense')} head={[t('reports.category'), t('reports.count'), t('reports.amount')]} empty={t('reports.noExpenses')}
            rows={(report.expenses ?? []).map((e) => ({ key: e.category, cells: [t(`expenses.cat.${e.category}`), e.count, n(e.amount)] }))} />
        </div>
      )}
    </main>
  );
}
