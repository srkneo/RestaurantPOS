import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import { formatMoney } from '../ui/format';
import './OrderHistory.css';

const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const qty = (n) => Number(n ?? 0).toLocaleString('en-IN', { maximumFractionDigits: 3 });

// The last 7 days, today included.
const lastWeek = () => {
  const today = new Date();
  const start = new Date(today);
  start.setDate(today.getDate() - 6);
  return { from: iso(start), to: iso(today) };
};

// Owner and manager: bought, used and wasted per stock item for a range of days, stock value now, supplier totals.
export default function StockReportPage() {
  const t = useT();
  const [range, setRange] = useState(lastWeek);
  const [state, setState] = useState({ status: 'loading' });

  const load = useCallback(async (r) => {
    setState({ status: 'loading' });
    try {
      const res = await fetch(`/api/stock/report?from=${r.from}&to=${r.to}`);
      const data = await res.json().catch(() => ({}));
      if (!res.ok) { setState({ status: 'error', message: data.message }); return; }
      setState({ status: 'ready', report: data });
    } catch {
      setState({ status: 'error' });
    }
  }, []);

  useEffect(() => { load(lastWeek()); }, [load]);

  const report = state.report;
  return (
    <main className="pos-page">
      <div className="pos-page-head"><h1>{t('stockReport.title')}</h1></div>
      <form className="d-flex flex-wrap align-items-end gap-2 mb-3" onSubmit={(e) => { e.preventDefault(); load(range); }}>
        <div>
          <label htmlFor="stock-report-from" className="form-label">{t('stockReport.from')}</label>
          <input id="stock-report-from" type="date" className="form-control" value={range.from} onChange={(e) => setRange({ ...range, from: e.target.value })} />
        </div>
        <div>
          <label htmlFor="stock-report-to" className="form-label">{t('stockReport.to')}</label>
          <input id="stock-report-to" type="date" className="form-control" value={range.to} onChange={(e) => setRange({ ...range, to: e.target.value })} />
        </div>
        <button type="submit" className="btn btn-primary px-4">{t('stockReport.show')}</button>
        <a href="/stock" className="btn btn-outline-secondary">{t('nav.stock')}</a>
      </form>

      {state.status === 'loading' && <StateMessage kind="loading" />}
      {state.status === 'error' && (state.message
        ? <p className="text-danger" role="alert">{state.message}</p>
        : <StateMessage kind="error" onRetry={() => load(range)} />)}
      {state.status === 'ready' && (
        <>
          <section className="pos-card p-3 mb-3" aria-label={t('stockReport.summary')}>
            <div className="d-flex flex-wrap gap-4">
              <div><div className="pos-hint">{t('stockReport.purchases')}</div><div className="fs-4 fw-bold">{formatMoney(report.purchaseCost)}</div></div>
              <div><div className="pos-hint">{t('stockReport.waste')}</div><div className="fs-4 fw-bold">{formatMoney(report.wasteValue)}</div></div>
              <div><div className="pos-hint">{t('stockReport.value')}</div><div className="fs-4 fw-bold">{formatMoney(report.stockValue)}</div></div>
            </div>
            <p className="pos-hint mb-0 mt-2">{t('stockReport.valueHint')}</p>
          </section>

          <section className="pos-card p-3 mb-3" aria-label={t('stockReport.items')}>
            <h2 className="h5">{t('stockReport.items')}</h2>
            {report.items.length === 0 ? <p className="pos-hint mb-0">{t('stockReport.empty')}</p> : (
              <div className="table-responsive">
                <table className="table align-middle mb-0">
                  <thead>
                    <tr>
                      <th scope="col">{t('stockReport.item')}</th>
                      <th scope="col" className="text-end">{t('stockReport.bought')}</th>
                      <th scope="col" className="text-end">{t('stockReport.cost')}</th>
                      <th scope="col" className="text-end">{t('stockReport.used')}</th>
                      <th scope="col" className="text-end">{t('stockReport.wasted')}</th>
                      <th scope="col" className="text-end">{t('stockReport.inStock')}</th>
                      <th scope="col" className="text-end">{t('stockReport.valueCol')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.items.map((i) => (
                      <tr key={i.id}>
                        <td>{i.name}</td>
                        <td className="text-end text-nowrap">{qty(i.bought)} {i.unit}</td>
                        <td className="text-end text-nowrap">{formatMoney(i.boughtCost)}</td>
                        <td className="text-end text-nowrap">{qty(i.used)} {i.unit}</td>
                        <td className="text-end text-nowrap">{qty(i.wasted)} {i.unit}</td>
                        <td className="text-end text-nowrap">{qty(i.quantity)} {i.unit}</td>
                        <td className="text-end text-nowrap">{i.value != null ? formatMoney(i.value) : '–'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="pos-card p-3" aria-label={t('stockReport.suppliers')}>
            <h2 className="h5">{t('stockReport.suppliers')}</h2>
            {report.suppliers.length === 0 ? <p className="pos-hint mb-0">{t('stockReport.empty')}</p> : (
              <div className="table-responsive">
                <table className="table align-middle mb-0">
                  <thead>
                    <tr>
                      <th scope="col">{t('stockReport.supplier')}</th>
                      <th scope="col" className="text-end">{t('stockReport.purchasesCount')}</th>
                      <th scope="col" className="text-end">{t('stockReport.cost')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.suppliers.map((s) => (
                      <tr key={s.supplier || '-'}>
                        <td>{s.supplier || t('stockReport.noSupplier')}</td>
                        <td className="text-end">{s.purchases}</td>
                        <td className="text-end">{formatMoney(s.cost)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}
    </main>
  );
}
