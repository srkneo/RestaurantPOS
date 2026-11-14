import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../AuthContext';
import { LanguageSwitch, useT } from '../i18n/I18nContext';

const Login = () => {
  const { login } = useAuth();
  const t = useT();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    setFailed(false);
    setBusy(true);
    let ok = false;
    try {
      ok = await login(email, password);
    } finally {
      setBusy(false);
    }
    if (ok) navigate('/', { replace: true });
    else setFailed(true);
  };

  return (
    <main className="pos-login">
      <form className="pos-login-card" onSubmit={submit}>
        <div className="d-flex justify-content-between align-items-center gap-2">
          <span className="pos-brand">{t('app.name')}</span>
          <LanguageSwitch />
        </div>
        <h1>{t('login.title')}</h1>
        <div>
          <label htmlFor="login-email" className="form-label">{t('login.email')}</label>
          <input id="login-email" type="email" className="form-control" autoComplete="username" autoFocus required
            value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div>
          <label htmlFor="login-password" className="form-label">{t('login.password')}</label>
          <input id="login-password" type="password" className="form-control" autoComplete="current-password" required
            value={password} onChange={(e) => setPassword(e.target.value)} />
        </div>
        {failed && <p className="text-danger mb-0" role="alert">{t('login.error')}</p>}
        <button type="submit" className="btn btn-primary btn-lg" disabled={busy}>
          {busy ? t('login.submitting') : t('login.submit')}
        </button>
      </form>
    </main>
  );
};

export default Login;
