import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { readErrors, send, Field } from '../ui/forms';
import { parseServerDate } from '../ui/format';
import ConfirmDialog from '../ui/ConfirmDialog';
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

  // Automatic updates (shop PC only): support's setting, the last result, and "Update now".
  const [updates, setUpdates] = useState(null);
  const [confirmUpdate, setConfirmUpdate] = useState(false);

  const loadUpdates = useCallback(async () => {
    try {
      const res = await fetch('/api/licence/updates');
      if (res.ok) setUpdates(await res.json());
    } catch {
      // Not shown when it cannot be read.
    }
  }, []);

  // Backups (shop PC only): the newest backup, the newest copy to the USB disk or NAS, and "Back up now".
  const [backups, setBackups] = useState(null);

  const loadBackups = useCallback(async () => {
    try {
      const res = await fetch('/api/licence/backups');
      if (res.ok) setBackups(await res.json());
    } catch {
      // Not shown when it cannot be read.
    }
  }, []);

  useEffect(() => {
    load();
    loadUpdates();
    loadBackups();
  }, [load, loadUpdates, loadBackups]);

  const backupNow = async () => {
    try {
      const res = await send('/api/licence/backups/run', 'POST');
      if (res.ok) {
        setBackups(await res.json());
        showToast(t('backup.requested'));
        window.dispatchEvent(new Event('backups-changed'));
      } else {
        const errors = await readErrors(res);
        showToast(errors.general || t('common.error'), 'error');
      }
    } catch {
      showToast(t('common.error'), 'error');
    }
  };

  const timeText = (value) => parseServerDate(value)?.toLocaleString() ?? t('backup.never');

  const updateNow = async () => {
    setConfirmUpdate(false);
    try {
      const res = await send('/api/licence/updates/run', 'POST');
      if (res.ok) {
        setUpdates(await res.json());
        showToast(t('licence.update.requested'));
      } else {
        const errors = await readErrors(res);
        showToast(errors.general || t('common.error'), 'error');
      }
    } catch {
      showToast(t('common.error'), 'error');
    }
  };

  const updateMode = (u) => (u.mode === 'pinned'
    ? t('licences.updates.pinned', { version: u.pinnedVersion || '' })
    : t(`licences.updates.${u.mode === 'paused' || u.mode === 'early' ? u.mode : 'auto'}`));

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
          <div className="pos-card p-3 p-md-4">
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
          {updates?.available === true && (
            <div className="pos-card d-flex flex-column gap-2">
              <h2 className="h5 mb-1">{t('licence.update.title')}</h2>
              <dl className="row mb-0">
                <dt className="col-sm-5">{t('licence.update.mode')}</dt>
                <dd className="col-sm-7">{updateMode(updates)}</dd>
                <dt className="col-sm-5">{t('licence.update.lastCheck')}</dt>
                <dd className="col-sm-7">
                  {updates.status ? (
                    <>
                      <span className="fw-semibold">{t(`licence.update.result.${updates.status.result}`)}</span>
                      {updates.status.checkedAt && <span className="text-body-secondary"> · {parseServerDate(updates.status.checkedAt)?.toLocaleString()}</span>}
                      {updates.status.message && <div className="small text-body-secondary">{updates.status.message}</div>}
                    </>
                  ) : t('licence.update.never')}
                </dd>
              </dl>
              {updates.mode === 'paused' && <p className="text-body-secondary mb-0">{t('licence.update.pausedHint')}</p>}
              {updates.requested && <p className="mb-0" role="status">{t('licence.update.requested')}</p>}
              <div>
                <button type="button" className="btn btn-outline-primary px-4" disabled={updates.mode === 'paused' || updates.requested}
                  onClick={() => setConfirmUpdate(true)}>
                  {t('licence.update.now')}
                </button>
              </div>
            </div>
          )}
          <ConfirmDialog open={confirmUpdate} title={t('licence.update.confirmTitle')} message={t('licence.update.confirmText')}
            confirmLabel={t('licence.update.confirm')} onConfirm={updateNow} onCancel={() => setConfirmUpdate(false)} />
          {backups?.available === true && (
            <div className="pos-card d-flex flex-column gap-2">
              <h2 className="h5 mb-1">{t('backup.title')}</h2>
              {backups.warning && <p className="text-danger fw-semibold mb-0" role="alert">{t('backup.warning')}</p>}
              <dl className="row mb-0">
                <dt className="col-sm-5">{t('backup.lastBackup')}</dt>
                <dd className="col-sm-7">{timeText(backups.backupAt)}</dd>
                <dt className="col-sm-5">{t('backup.lastCopy')}</dt>
                <dd className="col-sm-7">{timeText(backups.lastCopiedAt)}</dd>
              </dl>
              {backups.message && <p className="small text-body-secondary mb-0">{backups.message}</p>}
              <p className="small text-body-secondary mb-0">{t('backup.howTo')}</p>
              {backups.requested && <p className="mb-0" role="status">{t('backup.requested')}</p>}
              <div>
                <button type="button" className="btn btn-outline-primary px-4" disabled={backups.requested} onClick={backupNow}>
                  {t('backup.now')}
                </button>
              </div>
            </div>
          )}
        </div>
      )}
    </main>
  );
}
