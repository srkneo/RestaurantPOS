import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import { readErrors, send, Field } from '../ui/forms';

// Owner forgot the password on a shop PC: support reads a one-time code (made on the platform page) over the phone.
export default function ResetPasswordPage() {
  const t = useT();
  const navigate = useNavigate();
  const { showToast } = useToast();
  const [form, setForm] = useState({ email: '', code: '', password: '', repeat: '' });
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const setField = (name) => (e) => setForm({ ...form, [name]: e.target.value });

  const submit = async (e) => {
    e.preventDefault();
    const missing = {};
    if (form.email.trim() === '') missing.email = t('reset.required');
    if (form.code.trim() === '') missing.code = t('reset.required');
    if (form.password.length < 8) missing.password = t('reset.passwordShort');
    else if (form.password !== form.repeat) missing.repeat = t('reset.passwordsDiffer');
    setErrors(missing);
    if (Object.keys(missing).length > 0) return;

    setBusy(true);
    try {
      const res = await send('/api/auth/owner-reset', 'POST', { email: form.email.trim(), code: form.code.trim(), newPassword: form.password });
      if (res.ok) {
        showToast(t('reset.done'));
        navigate('/login', { replace: true });
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
        <h1>{t('reset.title')}</h1>
        <p className="text-body-secondary mb-0">{t('reset.intro')}</p>
        <Field id="reset-email" label={t('login.email')} error={errors.email}>
          <input id="reset-email" type="email" className="form-control" autoComplete="username" value={form.email} onChange={setField('email')} />
        </Field>
        <Field id="reset-code" label={t('reset.code')} hint={t('reset.codeHint')} error={errors.code}>
          <input id="reset-code" className="form-control" autoComplete="off" spellCheck={false} maxLength={20} value={form.code} onChange={setField('code')} />
        </Field>
        <Field id="reset-password" label={t('reset.newPassword')} hint={t('reset.passwordHint')} error={errors.password}>
          <input id="reset-password" type="password" className="form-control" autoComplete="new-password" value={form.password} onChange={setField('password')} />
        </Field>
        <Field id="reset-repeat" label={t('reset.repeat')} error={errors.repeat}>
          <input id="reset-repeat" type="password" className="form-control" autoComplete="new-password" value={form.repeat} onChange={setField('repeat')} />
        </Field>
        {errors.general && <p className="text-danger mb-0" role="alert">{errors.general}</p>}
        <button type="submit" className="btn btn-primary btn-lg" disabled={busy}>{t('reset.submit')}</button>
        <Link to="/login" className="text-center">{t('reset.back')}</Link>
      </form>
    </main>
  );
}
