import React from 'react';
import { useAuth } from '../AuthContext';
import { useT } from '../i18n/I18nContext';

export default function SupportBanner() {
  const { user, logout } = useAuth();
  const t = useT();

  if (!user || !user.roles.includes('support')) {
    return null;
  }

  const handleLogout = async () => {
    await logout();
    window.location.assign('/login');
  };

  return (
    <div role="alert" className="alert alert-danger d-flex align-items-center justify-content-between gap-3 mb-0 rounded-0">
      <span>{t('support.banner')}</span>
      <button type="button" className="btn btn-light btn-sm" onClick={handleLogout}>
        {t('support.end')}
      </button>
    </div>
  );
}
