// Shop PC: support signs in for 2 hours with today's code from the platform (SUP2).
import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { readErrors, send, Field } from '../ui/forms';

export default function SupportDayLoginPage() {
  const t = useT();
  const [code, setCode] = useState('');
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    if (code.trim() === '') {
      setErrors({ code: t('dayLogin.required') });
      return;
    }
    setErrors({});
    setBusy(true);
    try {
      const res = await send('/api/auth/support-day-login', 'POST', { code: code.trim() });
      if (res.ok) {
        window.location.assign('/');
      } else {
        const answer = await readErrors(res);
        setErrors({ general: answer.general || t('common.error') });
      }
    } catch {
      setErrors({ general: t('common.error') });
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="pos-login">
      <form className="pos-login-card" onSubmit={submit} noValidate>
        <h1>{t('dayLogin.title')}</h1>
        <p className="text-body-secondary mb-0">{t('dayLogin.intro')}</p>
        <Field id="day-code" label={t('dayLogin.code')} error={errors.code}>
          <input id="day-code" className="form-control" autoComplete="off" spellCheck={false} maxLength={20} value={code} onChange={(e) => setCode(e.target.value)} />
        </Field>
        {errors.general && <p className="text-danger mb-0" role="alert">{errors.general}</p>}
        <button type="submit" className="btn btn-primary btn-lg" disabled={busy}>{t('dayLogin.submit')}</button>
        <Link to="/login" className="text-center">{t('dayLogin.back')}</Link>
      </form>
    </main>
  );
}
