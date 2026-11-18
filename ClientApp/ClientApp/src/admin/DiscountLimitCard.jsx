import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import { send } from '../ui/forms';

// Owner: the most discount a cashier may give without a manager PIN (empty = no limit).
export default function DiscountLimitCard() {
  const t = useT();
  const { showToast } = useToast();
  const [value, setValue] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    let alive = true;
    fetch('/api/admin/discount-limit')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (alive && data) setValue(data.cashierMaxDiscountPercent == null ? '' : String(data.cashierMaxDiscountPercent));
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, []);

  const save = async (e) => {
    e.preventDefault();
    const text = value.trim();
    const limit = text === '' ? null : Number(text);
    if (limit !== null && (Number.isNaN(limit) || limit < 0 || limit > 100)) {
      setError(t('discountLimit.invalid'));
      return;
    }
    setError('');
    const res = await send('/api/admin/discount-limit', 'PUT', { cashierMaxDiscountPercent: limit }).catch(() => null);
    if (res?.ok) showToast(t('discountLimit.saved'));
    else setError(t('common.error'));
  };

  return (
    <form className="pos-card p-3 mt-3" onSubmit={save} aria-label={t('discountLimit.title')}>
      <h2 className="h5">{t('discountLimit.title')}</h2>
      <label htmlFor="discount-limit" className="form-label">{t('discountLimit.label')}</label>
      <div className="d-flex gap-2 align-items-start flex-wrap">
        <input id="discount-limit" className="form-control" style={{ maxWidth: 140 }} inputMode="decimal" placeholder={t('discountLimit.none')}
          value={value} onChange={(e) => setValue(e.target.value)} />
        <button type="submit" className="btn btn-outline-primary px-3">{t('discountLimit.save')}</button>
      </div>
      <p className="pos-hint mb-0 mt-2">{t('discountLimit.hint')}</p>
      {error && <div className="text-danger" role="alert">{error}</div>}
    </form>
  );
}
