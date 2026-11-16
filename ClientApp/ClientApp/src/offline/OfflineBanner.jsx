import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import { listOffline, syncOffline, retryOffline, removeOffline, CHANGED_EVENT } from './orderQueue';

export default function OfflineBanner() {
  const t = useT();
  const { showToast } = useToast();
  const [entries, setEntries] = useState([]);
  const [online, setOnline] = useState(navigator.onLine);

  const refresh = useCallback(async () => {
    setEntries(await listOffline());
  }, []);

  const send = useCallback(async () => {
    const result = await syncOffline();
    result.sent.forEach((s) => showToast(t('offline.sent', { token: s.tokenNumber })));
    if (result.refused.length > 0) {
      showToast(t('offline.refusedToast'), 'error');
    }
    await refresh();
  }, [refresh, showToast, t]);

  useEffect(() => {
    refresh();
    send();

    const goOnline = () => {
      setOnline(true);
      send();
    };

    const goOffline = () => {
      setOnline(false);
    };

    window.addEventListener('online', goOnline);
    window.addEventListener('offline', goOffline);
    window.addEventListener(CHANGED_EVENT, refresh);

    const timer = setInterval(send, 30000);

    return () => {
      window.removeEventListener('online', goOnline);
      window.removeEventListener('offline', goOffline);
      window.removeEventListener(CHANGED_EVENT, refresh);
      clearInterval(timer);
    };
  }, [refresh, send]);

  const waiting = entries.filter((e) => e.status === 'waiting');
  const refused = entries.filter((e) => e.status === 'refused');

  if (online && entries.length === 0) {
    return null;
  }

  return (
    <div className="alert alert-warning rounded-0 mb-0 py-2 d-flex flex-column gap-1" role="status">
      {!online && <div className="fw-semibold">{t('offline.noInternet')}</div>}
      {waiting.length > 0 && (
        <div className="d-flex align-items-center gap-2">
          <span>{t('offline.waiting', { count: waiting.length })}</span>
          <button type="button" className="btn btn-sm btn-outline-dark" onClick={send}>
            {t('offline.sendNow')}
          </button>
        </div>
      )}
      {refused.map((e) => (
        <div key={e.clientOrderId} className="d-flex flex-wrap align-items-center gap-2">
          <span>
            {t('offline.refused')} {e.body.customerName || (e.body.table ? `#${e.body.table}` : '')} – {e.error}
          </span>
          <button type="button" className="btn btn-sm btn-outline-dark" onClick={async () => { await retryOffline(e.clientOrderId); await send(); }}>
            {t('offline.retry')}
          </button>
          <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => removeOffline(e.clientOrderId)}>
            {t('offline.remove')}
          </button>
        </div>
      ))}
    </div>
  );
}
