import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import { send } from '../ui/forms';

// Owner: print a "Tell us how we did" QR on every bill. Guests rate 1-5 stars; see them under Feedback.
export default function FeedbackSettingCard() {
  const t = useT();
  const { showToast } = useToast();
  const [enabled, setEnabled] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    let alive = true;
    fetch('/api/admin/feedback-setting')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (alive && data && typeof data.enabled === 'boolean') setEnabled(data.enabled);
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, []);

  const change = async (e) => {
    const next = e.target.checked;
    setEnabled(next);
    const res = await send('/api/admin/feedback-setting', 'PUT', { enabled: next }).catch(() => null);
    if (res?.ok) {
      setError('');
      showToast(next ? t('feedback.on') : t('feedback.off'));
    } else {
      setEnabled(!next);
      setError(t('common.error'));
    }
  };

  return (
    <section className="pos-card p-3 d-flex flex-column gap-2" aria-label={t('feedback.settingTitle')}>
      <h2 className="h5 mb-0">{t('feedback.settingTitle')}</h2>
      <div className="form-check form-switch">
        <input id="feedback-on-bill" className="form-check-input" type="checkbox" role="switch" checked={enabled} onChange={change} />
        <label htmlFor="feedback-on-bill" className="form-check-label">{t('feedback.settingLabel')}</label>
      </div>
      <p className="pos-hint mb-0">{t('feedback.settingHint')}</p>
      {error && <div className="text-danger" role="alert">{error}</div>}
    </section>
  );
}
