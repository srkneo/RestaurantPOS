import React, { useEffect } from 'react';
import { useT } from '../i18n/I18nContext';

// Dialog with a form: fields as children, Save and Cancel buttons. Escape cancels.
export default function FormDialog({ open, title, submitLabel, busy = false, error, onCancel, onSubmit, children }) {
  const t = useT();

  useEffect(() => {
    if (!open) return undefined;
    const onKey = (e) => {
      if (e.key === 'Escape') onCancel?.();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onCancel]);

  if (!open) return null;

  return (
    <div className="pos-overlay">
      <form className="pos-dialog d-flex flex-column gap-3" role="dialog" aria-modal="true" aria-labelledby="pos-form-title" noValidate
        onSubmit={(e) => { e.preventDefault(); onSubmit?.(); }}>
        <h2 id="pos-form-title">{title}</h2>
        {children}
        {error && <p className="text-danger mb-0" role="alert">{error}</p>}
        <div className="pos-dialog-actions mt-0">
          <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>{t('common.cancel')}</button>
          <button type="submit" className="btn btn-primary" disabled={busy}>{submitLabel ?? t('common.confirm')}</button>
        </div>
      </form>
    </div>
  );
}
