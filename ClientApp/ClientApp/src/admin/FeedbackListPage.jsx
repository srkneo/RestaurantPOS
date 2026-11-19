import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import { parseServerDate } from '../ui/format';

const when = (value) => {
  const date = parseServerDate(value);
  return date ? new Intl.DateTimeFormat('en-IN', { timeZone: 'Asia/Kolkata', dateStyle: 'medium', timeStyle: 'short' }).format(date) : '';
};

// Owner and managers: what guests said through the feedback QR on their bills.
export default function FeedbackListPage() {
  const t = useT();
  const [data, setData] = useState(null);
  const [state, setState] = useState('loading');

  const load = useCallback(async () => {
    setState('loading');
    try {
      const res = await fetch('/api/admin/feedback');
      if (!res.ok) throw new Error(String(res.status));
      setData(await res.json());
      setState('ready');
    } catch {
      setState('error');
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  return (
    <main className="container py-3 d-flex flex-column gap-3">
      <div className="d-flex justify-content-between align-items-center flex-wrap gap-2">
        <h1 className="h3 mb-0">{t('feedback.title')}</h1>
        <a className="btn btn-outline-primary" href="/api/admin/feedback.csv" download>{t('feedback.csv')}</a>
      </div>
      {state === 'loading' && <StateMessage kind="loading" />}
      {state === 'error' && <StateMessage kind="error" onRetry={load} />}
      {state === 'ready' && data.count === 0 && <p className="pos-hint">{t('feedback.none')}</p>}
      {state === 'ready' && data.count > 0 && (
        <>
          <p className="h5 mb-0">{t('feedback.average', { average: Number(data.average).toFixed(1), count: data.count })}</p>
          <ul className="list-unstyled d-flex flex-column gap-2 mb-0">
            {data.items.map((f) => (
              <li key={f.id} className="pos-card p-3">
                <div className="d-flex justify-content-between flex-wrap gap-2">
                  <strong aria-label={t('feedback.stars', { count: f.rating })}>{'★'.repeat(f.rating)}{'☆'.repeat(5 - f.rating)}</strong>
                  <span className="pos-hint">{f.invoiceNumber} · {when(f.createdAt)}</span>
                </div>
                {f.comment && <p className="mb-0 mt-1">{f.comment}</p>}
              </li>
            ))}
          </ul>
        </>
      )}
    </main>
  );
}
