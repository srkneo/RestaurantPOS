import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { useAuth } from '../AuthContext';

// A bar under the header when this shop PC is locked or its licence ends soon. Shows nothing when no licence is needed.
export default function LicenceBanner() {
  const t = useT();
  const { user } = useAuth();
  const [status, setStatus] = useState(null);

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/licence/status');
      if (res.ok) setStatus(await res.json());
    } catch {
      // Offline or restarting: keep the last answer.
    }
  }, []);

  useEffect(() => {
    load();
    const timer = setInterval(load, 300000);
    window.addEventListener('online', load);
    window.addEventListener('licence-changed', load);
    return () => {
      clearInterval(timer);
      window.removeEventListener('online', load);
      window.removeEventListener('licence-changed', load);
    };
  }, [load]);

  if (!status || !status.required) return null;
  const locked = status.locked === true;
  if (!locked && status.state !== 'ExpiringSoon') return null;
  const isOwner = Boolean(user?.roles?.includes('admin'));

  return (
    <div className={`alert ${locked ? 'alert-danger' : 'alert-warning'} rounded-0 mb-0 py-2 d-flex flex-wrap align-items-center gap-2`}
      role={locked ? 'alert' : 'status'}>
      {locked && <strong>{t('licence.bannerLocked')}</strong>}
      <span>{status.message}</span>
      {isOwner && <Link to="/admin/licence" className="btn btn-sm btn-outline-dark">{t('licence.manage')}</Link>}
    </div>
  );
}
