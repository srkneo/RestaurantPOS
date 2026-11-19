import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import Pager, { usePaged } from '../ui/Pager';
import { parseServerDate } from '../ui/format';

const when = (value) => {
  const date = parseServerDate(value);
  return date ? new Intl.DateTimeFormat('en-IN', { timeZone: 'Asia/Kolkata', dateStyle: 'medium', timeStyle: 'short' }).format(date) : '';
};

const RATINGS = {
  all: () => true,
  low: (r) => r <= 2,
  mid: (r) => r === 3,
  high: (r) => r >= 4,
};

// Owner and managers: what guests said through the feedback QR on their bills.
// Search the comments and bill numbers, show only low or high ratings, 20 per page.
export default function FeedbackListPage() {
  const t = useT();
  const [data, setData] = useState(null);
  const [state, setState] = useState('loading');
  const [search, setSearch] = useState('');
  const [rating, setRating] = useState('all');

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

  const query = search.trim().toLowerCase();
  const filtered = (data?.items ?? []).filter((f) => RATINGS[rating](f.rating)
    && (!query || `${f.comment ?? ''} ${f.invoiceNumber ?? ''}`.toLowerCase().includes(query)));
  const { page, pageCount, pageItems, setPage, total } = usePaged(filtered, 20);

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
          <div className="d-flex flex-wrap align-items-end gap-2">
            <div>
              <label htmlFor="feedback-search" className="form-label">{t('feedback.search')}</label>
              <input id="feedback-search" type="search" className="form-control" value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
            <div>
              <label htmlFor="feedback-rating" className="form-label">{t('feedback.rating')}</label>
              <select id="feedback-rating" className="form-select" value={rating} onChange={(e) => setRating(e.target.value)}>
                <option value="all">{t('feedback.ratingAll')}</option>
                <option value="low">{t('feedback.ratingLow')}</option>
                <option value="mid">{t('feedback.ratingMid')}</option>
                <option value="high">{t('feedback.ratingHigh')}</option>
              </select>
            </div>
          </div>
          {filtered.length === 0 && <p className="pos-hint mb-0">{t('feedback.noMatch')}</p>}
          <ul className="list-unstyled d-flex flex-column gap-2 mb-0">
            {pageItems.map((f) => (
              <li key={f.id} className="pos-card p-3">
                <div className="d-flex justify-content-between flex-wrap gap-2">
                  <strong aria-label={t('feedback.stars', { count: f.rating })}>{'★'.repeat(f.rating)}{'☆'.repeat(5 - f.rating)}</strong>
                  <span className="pos-hint">{f.invoiceNumber} · {when(f.createdAt)}</span>
                </div>
                {f.comment && <p className="mb-0 mt-1">{f.comment}</p>}
              </li>
            ))}
          </ul>
          <Pager page={page} pageCount={pageCount} total={total} onPage={setPage} />
        </>
      )}
    </main>
  );
}
