import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import ConfirmDialog from '../ui/ConfirmDialog';
import { readErrors, send, Field } from '../ui/forms';

// Owner and managers: 2-step sign-in with a free authenticator app, and printed backup codes for a lost phone.
export default function SecurityPage() {
  const t = useT();
  const { showToast } = useToast();
  const [status, setStatus] = useState(null);
  const [loadState, setLoadState] = useState('loading');
  const [setup, setSetup] = useState(null);
  const [code, setCode] = useState('');
  const [codes, setCodes] = useState(null);
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const [askOff, setAskOff] = useState(false);
  const [pw, setPw] = useState({ current: '', next: '', again: '' });
  const [pwErrors, setPwErrors] = useState({});
  const [pwBusy, setPwBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/auth/2fa');
      if (!res.ok) throw new Error('load');
      setStatus(await res.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const call = async (url, body, onOk) => {
    setBusy(true);
    try {
      const res = await send(url, 'POST', body);
      if (!res.ok) { setErrors(await readErrors(res)); return; }
      setErrors({});
      setCode('');
      await onOk(res.status === 204 ? null : await res.json());
    } catch {
      setErrors({ general: t('pos.networkError') });
    } finally {
      setBusy(false);
    }
  };

  const startSetup = () => call('/api/auth/2fa/setup', {}, async (data) => { setCodes(null); setSetup(data); });
  const enable = () => call('/api/auth/2fa/enable', { code }, async (data) => {
    setSetup(null);
    setCodes(data.recoveryCodes);
    showToast(t('sec.turnedOn'));
    await load();
  });
  const newCodes = () => call('/api/auth/2fa/recovery-codes', { code }, async (data) => { setCodes(data.recoveryCodes); await load(); });
  const turnOff = () => {
    setAskOff(false);
    call('/api/auth/2fa/disable', { code }, async () => { setCodes(null); showToast(t('sec.turnedOff')); await load(); });
  };

  const codeField = (
    <Field id="sec-code" label={t('sec.code')} error={errors.code || (errors.general !== undefined ? (errors.general || t('common.error')) : undefined)}>
      <input id="sec-code" className="form-control" style={{ maxWidth: 220 }} inputMode="text" autoComplete="one-time-code" maxLength={11}
        value={code} onChange={(e) => setCode(e.target.value.replace(/[^0-9A-Za-z-]/g, ''))} />
    </Field>
  );

  return (
    <main className="pos-page">
      <div className="pos-page-head"><h1>{t('sec.title')}</h1></div>
      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && status && (
        <section className="pos-card p-3 p-md-4 d-flex flex-column gap-3" style={{ maxWidth: 680 }}>
          <h2 className="h5 mb-0">{t('sec.appTitle')}</h2>
          <p className="pos-hint mb-0">{t('sec.appHint')}</p>
          <p className="fw-semibold mb-0" role="status">{status.enabled ? t('sec.on', { left: status.recoveryCodesLeft }) : t('sec.off')}</p>

          {codes && (
            <div className="border rounded p-3">
              <p className="fw-semibold mb-1">{t('sec.codesTitle')}</p>
              <p className="pos-hint">{t('sec.codesHint')}</p>
              <ul className="list-unstyled d-grid gap-1 mb-2" style={{ gridTemplateColumns: 'repeat(2, max-content)', columnGap: 24 }}>
                {codes.map((c) => <li key={c}><code>{c}</code></li>)}
              </ul>
              <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => window.print()}>{t('sec.print')}</button>
            </div>
          )}

          {!status.enabled && !setup && (
            <button type="button" className="btn btn-primary px-3 align-self-start" disabled={busy} onClick={startSetup}>{t('sec.turnOn')}</button>
          )}

          {!status.enabled && setup && (
            <>
              <ol className="mb-0">
                <li>{t('sec.step1')}</li>
                <li>{t('sec.step2')}</li>
                <li>{t('sec.step3')}</li>
              </ol>
              <img src={`data:image/svg+xml;utf8,${encodeURIComponent(setup.qrSvg)}`} alt={t('sec.qrAlt')} width={220} height={220} />
              <p className="pos-hint mb-0">{t('sec.keyHint')} <code>{setup.key}</code></p>
              {codeField}
              <div className="d-flex flex-wrap gap-2">
                <button type="button" className="btn btn-primary px-3" disabled={busy || code.length < 6} onClick={enable}>{t('sec.confirm')}</button>
                <button type="button" className="btn btn-outline-secondary px-3" onClick={() => { setSetup(null); setErrors({}); }}>{t('common.cancel')}</button>
              </div>
            </>
          )}

          {status.enabled && (
            <>
              <p className="pos-hint mb-0">{t('sec.codeNeeded')}</p>
              {codeField}
              <div className="d-flex flex-wrap gap-2">
                <button type="button" className="btn btn-outline-secondary px-3" disabled={busy || code.length < 6} onClick={newCodes}>{t('sec.newCodes')}</button>
                <button type="button" className="btn btn-outline-danger px-3" disabled={busy || code.length < 6} onClick={() => setAskOff(true)}>{t('sec.turnOff')}</button>
              </div>
            </>
          )}
        </section>
      )}
      <section className="pos-card p-3 p-md-4 d-flex flex-column gap-3" style={{ maxWidth: 680 }}>
        <h2 className="h5 mb-0">{t('sec.passwordTitle')}</h2>
        <Field id="pw-current" label={t('sec.currentPassword')} error={pwErrors.currentpassword} autoComplete="current-password">
          <input id="pw-current" className="form-control" style={{ maxWidth: 220 }} inputMode="text" autoComplete="current-password"
            value={pw.current} onChange={(e) => setPw({ ...pw, current: e.target.value })} />
        </Field>
        <Field id="pw-new" label={t('sec.newPassword')} error={pwErrors.newpassword} autoComplete="new-password">
          <input id="pw-new" className="form-control" style={{ maxWidth: 220 }} inputMode="text" autoComplete="new-password"
            value={pw.next} onChange={(e) => setPw({ ...pw, next: e.target.value })} />
        </Field>
        <Field id="pw-again" label={t('sec.confirmPassword')} error={pwErrors.again} autoComplete="new-password">
          <input id="pw-again" className="form-control" style={{ maxWidth: 220 }} inputMode="text" autoComplete="new-password"
            value={pw.again} onChange={(e) => setPw({ ...pw, again: e.target.value })} />
        </Field>
        <button type="button" className="btn btn-primary align-self-start" disabled={pwBusy} onClick={async () => {
          if (pw.next !== pw.again) {
            setPwErrors({ again: t('sec.passwordMismatch') });
            return;
          }
          setPwBusy(true);
          try {
            const res = await send('/api/auth/password', 'POST', { currentPassword: pw.current, newPassword: pw.next });
            if (res.ok) {
              setPw({ current: '', next: '', again: '' });
              setPwErrors({});
              showToast(t('sec.passwordChanged'));
            } else {
              setPwErrors(await readErrors(res));
            }
          } finally {
            setPwBusy(false);
          }
        }}>{t('sec.changePassword')}</button>
      </section>
      <ConfirmDialog open={askOff} danger title={t('sec.offTitle')} message={t('sec.offText')} confirmLabel={t('sec.offYes')}
        onCancel={() => setAskOff(false)} onConfirm={turnOff} />
    </main>
  );
}
