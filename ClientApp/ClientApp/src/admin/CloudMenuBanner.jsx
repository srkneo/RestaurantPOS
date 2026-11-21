import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import { parseServerDate } from '../ui/format';

// v1.7 menu sync: on a shop PC whose menu is copied from the cloud, says so and offers to update it now.
// Shows nothing anywhere else (cloud server, shop PCs that keep their own menu).
export default function CloudMenuBanner({ onSynced }) {
  const t = useT();
  const { showToast } = useToast();
  const [status, setStatus] = useState(null);
  const [busy, setBusy] = useState(false);

  const managed = (data) => (data && data.managedByCloud === true ? data : null);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const res = await fetch('/api/admin/menu/sync');
        if (res.ok) {
          const data = managed(await res.json());
          if (alive) setStatus(data);
        }
      } catch {
        // No banner when the status cannot be read.
      }
    })();
    return () => { alive = false; };
  }, []);

  const syncNow = async () => {
    setBusy(true);
    try {
      const res = await fetch('/api/admin/menu/sync/now', { method: 'POST' });
      if (!res.ok) { showToast(t('common.error'), 'error'); return; }
      const data = await res.json();
      setStatus(managed(data));
      if (data?.error) showToast(t('menu.cloud.error', { error: data.error }), 'error');
      else { showToast(t('menu.cloud.done')); onSynced?.(); }
    } catch {
      showToast(t('common.error'), 'error');
    } finally {
      setBusy(false);
    }
  };

  if (!status) return null;
  const synced = parseServerDate(status.syncedAt);
  return (
    <div className="alert alert-info d-flex flex-wrap align-items-center justify-content-between gap-2 mb-0" role="status">
      <div>
        <div>{t('menu.cloud.banner')}</div>
        {synced && <small className="d-block">{t('menu.cloud.synced', { time: synced.toLocaleString() })}</small>}
        {status.error && <small className="d-block text-danger">{t('menu.cloud.error', { error: status.error })}</small>}
      </div>
      <button type="button" className="btn btn-outline-primary btn-sm px-3" onClick={syncNow} disabled={busy}>{t('menu.cloud.syncNow')}</button>
    </div>
  );
}
