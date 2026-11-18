import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import ConfirmDialog from '../ui/ConfirmDialog';
import StateMessage from '../ui/StateMessage';
import { send } from '../ui/forms';
import './QrSheetPage.css';

// Owner and manager: the QR code of every table, ready to print, and the switch for ordering by QR.
export default function QrSheetPage() {
  const t = useT();
  const { showToast } = useToast();
  const [tables, setTables] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [enabled, setEnabled] = useState(false);
  const [renewing, setRenewing] = useState(null);
  const origin = window.location.origin;

  const load = useCallback(async () => {
    try {
      const [tablesRes, settingRes] = await Promise.all([fetch('/api/admin/tables/qr'), fetch('/api/admin/tables/qr-setting')]);
      if (!tablesRes.ok) throw new Error('load');
      setTables(await tablesRes.json());
      if (settingRes.ok) setEnabled((await settingRes.json()).enabled);
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const toggle = async () => {
    const next = !enabled;
    setEnabled(next);
    const res = await send('/api/admin/tables/qr-setting', 'PUT', { enabled: next }).catch(() => null);
    if (res?.ok) showToast(t('qr.saved'));
    else {
      setEnabled(!next);
      showToast(t('common.error'), 'error');
    }
  };

  const renew = async () => {
    const table = renewing;
    setRenewing(null);
    const res = await send(`/api/admin/tables/${table.id}/qr`, 'POST', {}).catch(() => null);
    if (res?.ok) {
      showToast(t('qr.renewed', { number: table.tableNumber }));
      load();
    } else {
      showToast(t('common.error'), 'error');
    }
  };

  return (
    <main className="pos-page">
      <div className="pos-page-head qr-noprint">
        <h1>{t('qr.sheetTitle')}</h1>
        <div className="d-flex gap-2">
          <Link className="btn btn-outline-secondary px-3" to="/admin/tables">{t('qr.back')}</Link>
          <button type="button" className="btn btn-primary px-3" onClick={() => window.print()}>{t('qr.print')}</button>
        </div>
      </div>
      <div className="pos-card p-3 mb-3 qr-noprint">
        <div className="form-check form-switch pos-switch">
          <input className="form-check-input" type="checkbox" role="switch" id="qr-enabled" checked={enabled} onChange={toggle} />
          <label className="form-check-label fw-semibold" htmlFor="qr-enabled">{t('qr.on')}</label>
        </div>
        <p className="pos-hint mb-0">{t('qr.sheetHint')}</p>
      </div>

      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && tables.length === 0 && <StateMessage kind="empty" message={t('tables.empty')} />}
      <div className="qr-sheet">
        {tables.map((tb) => (
          <article key={tb.id} className="qr-card" aria-label={t('qr.table', { number: tb.tableNumber })}>
            <div className="qr-card-title">{t('qr.table', { number: tb.tableNumber })}</div>
            <img src={`/api/admin/tables/${tb.id}/qr.svg?origin=${encodeURIComponent(origin)}&v=${tb.token}`} alt={t('qr.table', { number: tb.tableNumber })} />
            <div className="qr-card-hint">{t('qr.scan')}</div>
            <button type="button" className="btn btn-outline-secondary btn-sm qr-noprint" onClick={() => setRenewing(tb)}>{t('qr.renew')}</button>
          </article>
        ))}
      </div>

      <ConfirmDialog open={!!renewing} title={t('qr.renewTitle', { number: renewing?.tableNumber ?? '' })} message={t('qr.renewHint')}
        confirmLabel={t('qr.renew')} onCancel={() => setRenewing(null)} onConfirm={renew} />
    </main>
  );
}
