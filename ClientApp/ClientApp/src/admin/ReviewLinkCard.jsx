import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import { send } from '../ui/forms';

// Owner: the Google review link. When set, every bill prints a "Rate us on Google" QR code.
export default function ReviewLinkCard() {
  const t = useT();
  const { showToast } = useToast();
  const [value, setValue] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    let alive = true;
    fetch('/api/admin/review-link')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (alive && data && typeof data.reviewUrl === 'string') setValue(data.reviewUrl);
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, []);

  const save = async (e) => {
    e.preventDefault();
    const url = value.trim();
    if (url && !/^https:\/\/\S+$/.test(url)) {
      setError(t('review.invalid'));
      return;
    }
    setError('');
    const res = await send('/api/admin/review-link', 'PUT', { reviewUrl: url || null }).catch(() => null);
    if (res?.ok) showToast(t('review.saved'));
    else setError(res?.status === 400 ? t('review.invalid') : t('common.error'));
  };

  return (
    <form className="pos-card p-3 d-flex flex-column gap-2" onSubmit={save} aria-label={t('review.title')}>
      <h2 className="h5 mb-0">{t('review.title')}</h2>
      <label htmlFor="review-url" className="form-label mb-0">{t('review.label')}</label>
      <input id="review-url" className="form-control" inputMode="url" placeholder="https://g.page/r/..." value={value}
        onChange={(e) => setValue(e.target.value)} />
      <p className="pos-hint mb-0">{t('review.hint')}</p>
      {error && <div className="text-danger" role="alert">{error}</div>}
      <button type="submit" className="btn btn-outline-primary px-3 align-self-start">{t('review.save')}</button>
    </form>
  );
}
