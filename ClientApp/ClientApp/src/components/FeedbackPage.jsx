import React, { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { LanguageSwitch, useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import './GuestOrderPage.css';

const STARS = [1, 2, 3, 4, 5];

// Guest page behind the feedback QR on a bill (/feedback/<token>): no sign-in. 1-5 stars and an optional comment, once per bill.
export default function FeedbackPage() {
  const t = useT();
  const { token } = useParams();
  const [form, setForm] = useState(null);
  const [state, setState] = useState('loading'); // loading | ready | notfound | error | thanks | given
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState('');
  const [error, setError] = useState('');
  const [sending, setSending] = useState(false);

  const load = useCallback(async () => {
    setState('loading');
    try {
      const res = await fetch(`/api/feedback/${encodeURIComponent(token)}`);
      if (res.status === 404) { setState('notfound'); return; }
      if (!res.ok) { setState('error'); return; }
      const data = await res.json();
      setForm(data);
      setState(data.alreadyGiven ? 'given' : 'ready');
    } catch {
      setState('error');
    }
  }, [token]);

  useEffect(() => { load(); }, [load]);

  const send = async (e) => {
    e.preventDefault();
    if (rating < 1) { setError(t('feedback.chooseStars')); return; }
    setError('');
    setSending(true);
    try {
      const res = await fetch(`/api/feedback/${encodeURIComponent(token)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ rating, comment: comment.trim() || null }),
      });
      if (res.ok) setState('thanks');
      else if (res.status === 409) setState('given');
      else if (res.status === 429) setError(t('feedback.tooMany'));
      else setError(t('common.error'));
    } catch {
      setError(t('common.error'));
    } finally {
      setSending(false);
    }
  };

  if (state === 'loading') return <StateMessage kind="loading" />;
  if (state === 'error') return <StateMessage kind="error" onRetry={load} />;
  if (state === 'notfound') {
    return <main className="guest"><p className="guest-note" role="status">{t('feedback.notFound')}</p></main>;
  }

  return (
    <main className="guest">
      <header className="guest-head">
        <div>
          <h1>{form.restaurantName}</h1>
          <div className="guest-desc">{t('feedback.bill', { number: form.invoiceNumber })}</div>
        </div>
        <LanguageSwitch />
      </header>

      {state === 'thanks' && <p className="guest-message ok mt-3" role="status">{t('feedback.thanks')}</p>}
      {state === 'given' && <p className="guest-message ok mt-3" role="status">{t('feedback.given')}</p>}

      {state === 'ready' && (
        <form className="d-flex flex-column gap-3 mt-3" onSubmit={send}>
          <fieldset>
            <legend className="h5">{t('feedback.question')}</legend>
            <div className="d-flex gap-2" role="group" aria-label={t('feedback.question')}>
              {STARS.map((n) => (
                <button key={n} type="button" className={`btn guest-add ${n <= rating ? 'btn-warning' : 'btn-outline-secondary'}`}
                  aria-pressed={n === rating} aria-label={t('feedback.stars', { count: n })} onClick={() => setRating(n)}>★</button>
              ))}
            </div>
          </fieldset>
          <div>
            <label htmlFor="feedback-comment" className="form-label">{t('feedback.comment')}</label>
            <textarea id="feedback-comment" className="form-control" rows={3} maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} />
          </div>
          {error && <p className="guest-message error mb-0" role="alert">{error}</p>}
          <button type="submit" className="btn btn-primary btn-lg" disabled={sending}>{t('feedback.send')}</button>
        </form>
      )}
    </main>
  );
}
