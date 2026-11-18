import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';

// For the owner of a shop PC: a bar when the last backup, or the last copy to the USB disk or NAS, is more than 2 days old.
export default function BackupBanner() {
  const t = useT();
  const [status, setStatus] = useState(null);

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/licence/backups');
      if (res.ok) setStatus(await res.json());
    } catch {
      // Offline or restarting: keep the last answer.
    }
  }, []);

  useEffect(() => {
    load();
    const timer = setInterval(load, 1800000);
    window.addEventListener('backups-changed', load);
    return () => {
      clearInterval(timer);
      window.removeEventListener('backups-changed', load);
    };
  }, [load]);

  if (!status?.available || !status.warning) return null;
  return (
    <div className="alert alert-warning rounded-0 mb-0 py-2 d-flex flex-wrap align-items-center gap-2" role="status">
      <strong>{t('backup.bannerTitle')}</strong>
      <span>{status.result === 'no-disk' ? t('backup.bannerNoDisk') : t('backup.bannerOld')}</span>
      <Link to="/admin/licence" className="btn btn-sm btn-outline-dark">{t('backup.bannerAction')}</Link>
    </div>
  );
}
