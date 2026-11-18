import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import { send } from '../ui/forms';

// Owner: which cashier actions need a manager PIN (printing a bill again, for now).
export default function ApprovalRulesCard() {
  const t = useT();
  const { showToast } = useToast();
  const [reprintNeedsPin, setReprintNeedsPin] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    let alive = true;
    fetch('/api/admin/approval-rules')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (alive && data && typeof data.reprintNeedsPin === 'boolean') setReprintNeedsPin(data.reprintNeedsPin);
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, []);

  const change = async (e) => {
    const next = e.target.checked;
    setReprintNeedsPin(next);
    const res = await send('/api/admin/approval-rules', 'PUT', { reprintNeedsPin: next }).catch(() => null);
    if (res?.ok) {
      setError('');
      showToast(t('approvalRules.saved'));
    } else {
      setReprintNeedsPin(!next);
      setError(t('common.error'));
    }
  };

  return (
    <section className="pos-card p-3 mt-3 d-flex flex-column gap-2" aria-label={t('approvalRules.title')}>
      <h2 className="h5 mb-0">{t('approvalRules.title')}</h2>
      <div className="form-check form-switch">
        <input id="reprint-needs-pin" className="form-check-input" type="checkbox" role="switch" checked={reprintNeedsPin} onChange={change} />
        <label htmlFor="reprint-needs-pin" className="form-check-label">{t('approvalRules.reprint')}</label>
      </div>
      <p className="pos-hint mb-0">{t('approvalRules.hint')}</p>
      {error && <div className="text-danger" role="alert">{error}</div>}
    </section>
  );
}
