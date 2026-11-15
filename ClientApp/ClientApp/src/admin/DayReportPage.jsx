import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import DayReport from '../print/DayReport';
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
      const [reportRes, settingsRes] = await Promise.all([fetch(`/api/reports/day${query}`), fetch('/api/print-settings')]);
      if (!reportRes.ok) {
        setState({ status: 'error' });
        return;
      }
      const report = await reportRes.json();
      const settings = settingsRes.ok ? await settingsRes.json() : { printLanguage: 'en', paperWidth: 80 };
      setState({ status: 'ready', report, settings });
      setDate(report.businessDate);
    } catch {
      setState({ status: 'error' });
    }
  }, []);

  useEffect(() => { load(''); }, [load]);

  const print = () => window.open(`/print/day?date=${state.report.businessDate}&auto=1`, '_blank');

  return (
    <main className="pos-page">
      <div className="pos-page-head"><h1>{t('dayReport.title')}</h1></div>
      <form className="day-report-bar" onSubmit={(e) => { e.preventDefault(); load(date); }}>
        <label htmlFor="report-date" className="fw-semibold">{t('dayReport.date')}</label>
        <input id="report-date" type="date" className="form-control" value={date} onChange={(e) => setDate(e.target.value)} />
        <button type="submit" className="btn btn-outline-secondary">{t('dayReport.show')}</button>
        {state.status === 'ready' && <button type="button" className="btn btn-primary px-4" onClick={print}>{t('dayReport.print')}</button>}
      </form>
      {state.status === 'loading' && <StateMessage kind="loading" />}
      {state.status === 'error' && <StateMessage kind="error" onRetry={() => load(date)} />}
      {state.status === 'ready' && (
        <div className="day-report-sheet">
          <DayReport report={state.report} lang={state.settings.printLanguage} width={state.settings.paperWidth} />
        </div>
      )}
    </main>
  );
}
