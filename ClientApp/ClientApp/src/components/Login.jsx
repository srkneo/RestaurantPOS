import React, { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../AuthContext';
import { LanguageSwitch, useT } from '../i18n/I18nContext';

// Sign in. The platform owner gets a second step: the 6-digit code the server sends to Telegram.
const Login = () => {
  const { login, loginCode } = useAuth();
  const t = useT();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [challenge, setChallenge] = useState(null);
  const [code, setCode] = useState('');
  const [method, setMethod] = useState('telegram');   // telegram (platform owner) or app (authenticator app)
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [signupOpen, setSignupOpen] = useState(false);

  // Cloud only: offer the free trial to new restaurants (a shop PC says signup is closed).
  useEffect(() => {
    let alive = true;
    fetch('/api/signup/plans')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => { if (alive) setSignupOpen(data?.signupOpen === true); })
      .catch(() => {});
    return () => { alive = false; };
  }, []);

  const submit = async (e) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    let result = false;
    try {
      result = challenge ? await loginCode(challenge, code.trim()) : await login(email, password);
    } finally {
      setBusy(false);
    }
    if (result === true) {
      navigate('/', { replace: true });
    } else if (result?.twoStep) {
      setChallenge(result.challenge);
      setMethod(result.method ?? 'telegram');
      setCode('');
    } else if (result?.sendFailed) {
      setError(t('login.sendFailed'));
    } else {
      setError(challenge ? t('login.codeError') : t('login.error'));
    }
  };

  const startAgain = () => {
    setChallenge(null);
    setCode('');
    setPassword('');
    setError(null);
  };

  return (
    <main className="pos-login">
      <form className="pos-login-card" onSubmit={submit}>
        <div className="d-flex justify-content-between align-items-center gap-2">
          <span className="pos-brand">{t('app.name')}</span>
          <LanguageSwitch />
        </div>
        <h1>{challenge ? t('login.codeTitle') : t('login.title')}</h1>
        {!challenge && (
          <>
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
          </>
        )}
        {challenge && (
          <div>
            <label htmlFor="login-code" className="form-label">{method === 'app' ? t('login.appCode') : t('login.code')}</label>
            <input id="login-code" className="form-control form-control-lg" inputMode={method === 'app' ? 'text' : 'numeric'} autoComplete="one-time-code"
              maxLength={method === 'app' ? 11 : 6} autoFocus required value={code}
              onChange={(e) => setCode(method === 'app' ? e.target.value.replace(/[^0-9A-Za-z-]/g, '') : e.target.value.replace(/\D/g, ''))} />
            <div className="form-text">{method === 'app' ? t('login.appCodeHint') : t('login.codeHint')}</div>
          </div>
        )}
        {error && <p className="text-danger mb-0" role="alert">{error}</p>}
        <button type="submit" className="btn btn-primary btn-lg" disabled={busy}>
          {busy ? t('login.submitting') : challenge ? t('login.verify') : t('login.submit')}
        </button>
        {challenge
          ? <button type="button" className="btn btn-link" onClick={startAgain}>{t('login.startAgain')}</button>
          : <Link to="/reset-password" className="text-center">{t('login.forgot')}</Link>}
        {!challenge && <Link to="/support-day-login" className="text-center small text-body-secondary">{t('login.supportDayCode')}</Link>}
        {!challenge && signupOpen && <Link to="/signup" className="text-center">{t('signup.link')}</Link>}
        {!challenge && signupOpen && <Link to="/pricing" className="text-center">{t('pricing.link')}</Link>}
      </form>
    </main>
  );
};

export default Login;
