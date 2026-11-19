import React, { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../AuthContext';
import { LanguageSwitch, useT } from '../i18n/I18nContext';
import { readErrors, send, Field } from '../ui/forms';
import { formatMoney } from '../ui/format';

const EMPTY = { restaurantName: '', ownerName: '', ownerEmail: '', ownerPhone: '', ownerPassword: '', website: '' };

// Cloud only: a new restaurant starts a free trial by itself, then lands on the setup checklist.
export default function SignupPage() {
  const t = useT();
  const { login } = useAuth();
  const navigate = useNavigate();
  const [plans, setPlans] = useState(null);
  const [form, setForm] = useState(EMPTY);
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    fetch('/api/signup/plans')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => { if (alive) setPlans(data); })
      .catch(() => {});
    return () => { alive = false; };
  }, []);

  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value });

  const submit = async (e) => {
    e.preventDefault();
    setErrors({});
    setBusy(true);
    try {
      const res = await send('/api/signup', 'POST', form);
      if (!res.ok) {
        setErrors(await readErrors(res));
        return;
      }
      const signedIn = await login(form.ownerEmail.trim(), form.ownerPassword);
      navigate(signedIn === true ? '/admin/setup' : '/login', { replace: true });
    } catch {
      setErrors({ general: '' });
    } finally {
      setBusy(false);
    }
  };

  const closed = plans?.signupOpen === false;

  return (
    <main className="pos-login">
      <form className="pos-login-card" onSubmit={submit} noValidate>
        <div className="d-flex justify-content-between align-items-center gap-2">
          <span className="pos-brand">{t('app.name')}</span>
          <LanguageSwitch />
        </div>
        <h1>{t('signup.title')}</h1>
        {plans && !closed && (
          <p className="pos-hint mb-0">
            {t('signup.plans', { days: plans.trialDays, monthly: formatMoney(plans.monthly), yearly: formatMoney(plans.yearly) })}
          </p>
        )}
        {closed ? (
          <p className="mb-0" role="alert">{t('signup.closed')}</p>
        ) : (
          <>
            <Field id="signup-restaurant" label={t('signup.restaurantName')} error={errors.restaurantname}>
              <input id="signup-restaurant" className="form-control" maxLength={200} required value={form.restaurantName} onChange={set('restaurantName')} />
            </Field>
            <Field id="signup-name" label={t('signup.ownerName')} error={errors.ownername}>
              <input id="signup-name" className="form-control" maxLength={100} autoComplete="name" required value={form.ownerName} onChange={set('ownerName')} />
            </Field>
            <Field id="signup-email" label={t('signup.ownerEmail')} error={errors.owneremail}>
              <input id="signup-email" type="email" className="form-control" autoComplete="username" required value={form.ownerEmail} onChange={set('ownerEmail')} />
            </Field>
            <Field id="signup-phone" label={t('signup.ownerPhone')} error={errors.ownerphone}>
              <input id="signup-phone" type="tel" inputMode="tel" className="form-control" autoComplete="tel" required value={form.ownerPhone} onChange={set('ownerPhone')} />
            </Field>
            <Field id="signup-password" label={t('signup.ownerPassword')} hint={t('signup.passwordHint')} error={errors.ownerpassword}>
              <input id="signup-password" type="password" className="form-control" autoComplete="new-password" required value={form.ownerPassword} onChange={set('ownerPassword')} />
            </Field>
            {/* Left empty by people; bots fill every field. */}
            <input name="website" tabIndex={-1} autoComplete="off" aria-hidden="true" style={{ display: 'none' }} value={form.website} onChange={set('website')} />
            {errors.general !== undefined && <p className="text-danger mb-0" role="alert">{errors.general || t('common.error')}</p>}
            <p className="small mb-0">
              {t('signup.agree')}{' '}
              <a href="/terms" target="_blank" rel="noreferrer">{t('legal.terms')}</a>{' · '}
              <a href="/privacy" target="_blank" rel="noreferrer">{t('legal.privacy')}</a>
            </p>
            <button type="submit" className="btn btn-primary btn-lg" disabled={busy}>{busy ? t('signup.creating') : t('signup.submit')}</button>
          </>
        )}
        <Link to="/login" className="text-center">{t('signup.haveAccount')}</Link>
      </form>
    </main>
  );
}
