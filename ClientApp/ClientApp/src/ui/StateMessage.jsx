import React from 'react';
import { useT } from '../i18n/I18nContext';

// One look for loading, empty and error states: <StateMessage kind="loading|empty|error" onRetry={...} />
export default function StateMessage({ kind = 'empty', message, onRetry }) {
  const t = useT();
  const fallback = kind === 'loading' ? t('common.loading') : kind === 'error' ? t('common.error') : t('common.empty');
  return (
    <div className={`pos-state pos-state-${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      {kind === 'loading' && <div className="spinner-border" aria-hidden="true" />}
      <p className="mb-0">{message ?? fallback}</p>
      {kind === 'error' && onRetry && (
        <button type="button" className="btn btn-outline-secondary" onClick={onRetry}>{t('common.retry')}</button>
      )}
    </div>
  );
}
