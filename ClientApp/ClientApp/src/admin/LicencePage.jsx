import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { readErrors, send, Field } from '../ui/forms';
import { parseServerDate } from '../ui/format';
import '../components/OrderHistory.css';

const dateText = (value) => {
  const date = parseServerDate(value);
  return date ? date.toLocaleDateString() : '–';
};

// The owner's licence page on a shop PC: see the licence, enter the key, refresh it from the licence server.
export default function LicencePage() {
  const t = useT();
  const { showToast } = useToast();

  const [status, setStatus] = useState(null);
  const [loadState, setLoadState] = useState('loading');
  const [key, setKey] = useState('');
  const [error, setError] = useState('');
  const [keyError, setKeyError] = useState('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/licence/status');
      if (res.ok) {
        setStatus(await res.json());
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

  const call = async (url, body, doneKey) => {
    setBusy(true);
    setError('');
    try {
      const res = await send(url, 'POST', body);
      if (res.ok) {
        setStatus(await res.json());
        setKey('');
        // The banner at the top checks again, so it does not keep saying billing is stopped.
        window.dispatchEvent(new Event('licence-changed'));
        showToast(t(doneKey));
      } else {
        const errors = await readErrors(res);
        setError(errors.general || t('common.error'));
      }
    } catch {
      setError(t('common.error'));
    } finally {
      setBusy(false);
    }
  };

  const activate = (e) => {
    e.preventDefault();
    if (key.trim() === '') {
      setKeyError(t('licence.keyRequired'));
      return;
    }
    setKeyError('');
    call('/api/licence/activate', { key: key.trim() }, 'licence.activated');
  };

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('licence.title')}</h1>
      </div>
      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && !status.required && <StateMessage kind="empty" message={t('licence.notRequired')} />}
      {loadState === 'ready' && status.required && (
        <div className="d-flex flex-column gap-3" style={{ maxWidth: 640 }}>
          <div className="pos-card">
            <dl className="row mb-2">
              <dt className="col-sm-5">{t('licence.status')}</dt>
              <dd className="col-sm-7 fw-semibold">{t(`licence.state.${status.state}`)}</dd>
              {status.customer && (
                <>
                  <dt className="col-sm-5">{t('licence.customer')}</dt>
                  <dd className="col-sm-7">{status.customer}</dd>
                </>
              )}
              {status.plan && (
                <>
                  <dt className="col-sm-5">{t('licence.plan')}</dt>
                  <dd className="col-sm-7">{t(`licence.plan.${status.plan}`)}</dd>
                </>
              )}
              {status.paidUntil && (
                <>
                  <dt className="col-sm-5">{t('licence.paidUntil')}</dt>
                  <dd className="col-sm-7">{dateText(status.paidUntil)}</dd>
                </>
              )}
              {status.leaseUntil && (
                <>
                  <dt className="col-sm-5">{t('licence.leaseUntil')}</dt>
                  <dd className="col-sm-7">{dateText(status.leaseUntil)}</dd>
                </>
              )}
            </dl>
            <p className={`mb-0 ${status.locked ? 'text-danger' : ''}`}>{status.message}</p>
          </div>
          <form className="pos-card d-flex flex-column gap-3" onSubmit={activate} noValidate>
            <Field id="licence-key" label={t('licence.keyLabel')} hint={t('licence.keyHint')} error={keyError}>
              <input className="form-control" id="licence-key" value={key} onChange={(e) => setKey(e.target.value)}
                autoComplete="off" spellCheck={false} maxLength={60} />
            </Field>
            {error && <p className="text-danger mb-0" role="alert">{error}</p>}
            <div className="d-flex gap-2">
              <button type="submit" className="btn btn-primary px-4" disabled={busy}>{t('licence.activate')}</button>
              <button type="button" className="btn btn-outline-secondary px-4" disabled={busy}
                onClick={() => call('/api/licence/refresh', undefined, 'licence.refreshed')}>
                {t('licence.refresh')}
              </button>
            </div>
          </form>
        </div>
      )}
    </main>
  );
}
