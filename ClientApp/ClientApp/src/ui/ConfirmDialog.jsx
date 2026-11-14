import React, { useEffect, useRef } from 'react';
import { useT } from '../i18n/I18nContext';

// Ask before delete, void or cancel. Escape or a click outside cancels.
export default function ConfirmDialog({ open, title, message, confirmLabel, danger = false, onConfirm, onCancel }) {
  const t = useT();
  const cancelRef = useRef(null);

  useEffect(() => {
    if (!open) return undefined;
    cancelRef.current?.focus();
    const onKey = (e) => {
      if (e.key === 'Escape') onCancel?.();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onCancel]);

  if (!open) return null;

  return (
    <div className="pos-overlay" onClick={onCancel}>
      <div className="pos-dialog" role="dialog" aria-modal="true" aria-labelledby="pos-dialog-title" onClick={(e) => e.stopPropagation()}>
        <h2 id="pos-dialog-title">{title}</h2>
        {message && <p className="mb-0">{message}</p>}
        <div className="pos-dialog-actions">
          <button ref={cancelRef} type="button" className="btn btn-outline-secondary" onClick={onCancel}>
            {t('common.cancel')}
          </button>
          <button type="button" className={`btn ${danger ? 'btn-danger' : 'btn-primary'}`} onClick={onConfirm}>
            {confirmLabel ?? t('common.confirm')}
          </button>
        </div>
      </div>
    </div>
  );
}
