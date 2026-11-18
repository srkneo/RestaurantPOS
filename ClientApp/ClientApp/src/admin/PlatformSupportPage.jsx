import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { readErrors, send, Field } from '../ui/forms';
import { loadAbout } from '../about/about';
import '../components/OrderHistory.css';

const FIELDS = [
  { name: 'companyName', label: 'support.companyName', required: true, max: 100 },
  { name: 'productName', label: 'support.productName', required: true, max: 60 },
  { name: 'phone', label: 'support.phone', hint: 'support.phoneHint', type: 'tel', max: 20 },
  { name: 'whatsApp', label: 'support.whatsApp', hint: 'support.whatsAppHint', type: 'tel', max: 20 },
  { name: 'email', label: 'support.email', type: 'email', max: 200 },
  { name: 'hours', label: 'support.hours', hint: 'support.hoursHint', max: 100 },
  { name: 'website', label: 'support.website', hint: 'support.websiteHint', type: 'url', max: 200 },
];

const EMPTY = Object.fromEntries(FIELDS.map((f) => [f.name, '']));

// The POS owner's screen: the support details every restaurant sees in the footer and on the About page.
export default function PlatformSupportPage() {
  const t = useT();
  const { showToast } = useToast();
  const [form, setForm] = useState(EMPTY);
  const [loadState, setLoadState] = useState('loading');
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/platform/support');
      if (res.ok) {
        const data = await res.json();
        setForm(Object.fromEntries(FIELDS.map((f) => [f.name, data[f.name] ?? ''])));
        setLoadState('ready');
      } else {
        setLoadState('error');
      }
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const save = async (e) => {
    e.preventDefault();
    const missing = {};
    FIELDS.filter((f) => f.required && form[f.name].trim() === '').forEach((f) => {
      missing[f.name.toLowerCase()] = t('support.required');
    });
    setErrors(missing);
    if (Object.keys(missing).length > 0) return;

    setBusy(true);
    try {
      const body = Object.fromEntries(FIELDS.map((f) => [f.name, form[f.name].trim()]));
      const res = await send('/api/platform/support', 'PUT', body);
      if (res.ok) {
        const data = await res.json();
        setForm(Object.fromEntries(FIELDS.map((f) => [f.name, data[f.name] ?? ''])));
        loadAbout(true);
        showToast(t('support.saved'));
      } else {
        setErrors(await readErrors(res));
      }
    } catch {
      setErrors({ general: t('common.error') });
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('support.title')}</h1>
      </div>
      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && (
        <form className="pos-card d-flex flex-column gap-3" style={{ maxWidth: 640 }} onSubmit={save} noValidate>
          <p className="text-body-secondary mb-0">{t('support.intro')}</p>
          {FIELDS.map((f) => (
            <Field key={f.name} id={`support-${f.name}`} label={t(f.label)} hint={f.hint ? t(f.hint) : undefined}
              error={errors[f.name.toLowerCase()]}>
              <input className="form-control" id={`support-${f.name}`} type={f.type || 'text'} maxLength={f.max}
                value={form[f.name]} onChange={(e) => setForm({ ...form, [f.name]: e.target.value })} />
            </Field>
          ))}
          {errors.general && <p className="text-danger mb-0" role="alert">{errors.general}</p>}
          <div>
            <button type="submit" className="btn btn-primary px-4" disabled={busy}>{t('support.save')}</button>
          </div>
        </form>
      )}
    </main>
  );
}
