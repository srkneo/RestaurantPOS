import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import { formatMoney } from '../ui/format';

// Loyalty on the bill: the guest's points and "Use points", or "Add to loyalty" (asks consent) for a mobile number not in the list.
// Shows nothing when loyalty is off or the order has no mobile number.
export default function LoyaltyPanel({ phone, name, due, canUse, busy, refreshKey, onUse }) {
  const t = useT();
  const { showToast } = useToast();
  const [settings, setSettings] = useState(null);
  const [guest, setGuest] = useState(undefined);   // undefined while loading, null when not a loyalty guest
  const [joining, setJoining] = useState(false);
  const [joinName, setJoinName] = useState(name ?? '');
  const [consent, setConsent] = useState(false);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let alive = true;
    fetch('/api/customers/settings').then((res) => (res.ok ? res.json() : null))
      .then((s) => { if (alive) setSettings(s); })
      .catch(() => {});
    return () => { alive = false; };
  }, []);

  useEffect(() => {
    if (!settings?.enabled || !phone) return undefined;
    let alive = true;
    fetch(`/api/customers/by-phone?phone=${encodeURIComponent(phone)}`)
      .then(async (res) => {
        if (!alive) return;
        if (res.ok) setGuest(await res.json());
        else if (res.status === 404) setGuest(null);
      })
      .catch(() => {});
    return () => { alive = false; };
  }, [settings, phone, refreshKey]);

  if (!settings?.enabled || !phone || guest === undefined) return null;

  const join = async () => {
    if (!consent) { setError(t('cust.consentNeeded')); return; }
    setSaving(true);
    try {
      const res = await fetch('/api/customers', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: joinName.trim(), phone, consent: true }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        const first = data?.errors ? Object.values(data.errors)[0]?.[0] : null;
        setError(first || t('common.error'));
        return;
      }
      setGuest(data);
      setJoining(false);
      setError('');
      showToast(t('loyalty.joined', { name: data.name }));
    } catch {
      setError(t('pos.networkError'));
    } finally {
      setSaving(false);
    }
  };

  const value = Number(settings.pointValue) || 0;
  const usable = guest && value > 0 ? Math.max(0, Math.min(guest.points, Math.floor(due / value + 1e-9))) : 0;
  const worth = Math.round(usable * value * 100) / 100;

  return (
    <div className="pos-bill-loyalty d-flex flex-column gap-2">
      {guest && (
        <>
          <p className="mb-0 fw-semibold">{t('loyalty.guest', { name: guest.name, points: guest.points })}</p>
          {canUse && usable > 0 && (
            <button type="button" className="btn btn-outline-primary align-self-start" disabled={busy} onClick={() => onUse(worth)}>
              {t('loyalty.use', { points: usable, amount: formatMoney(worth) })}
            </button>
          )}
        </>
      )}
      {guest === null && !joining && (
        <div className="d-flex align-items-center flex-wrap gap-2">
          <span className="pos-hint">{t('loyalty.notGuest')}</span>
          <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setJoining(true)}>{t('loyalty.join')}</button>
        </div>
      )}
      {guest === null && joining && (
        <div className="d-flex flex-column gap-2">
          <div>
            <label htmlFor="loy-join-name" className="form-label">{t('cust.name')}</label>
            <input id="loy-join-name" className="form-control" maxLength={60} autoComplete="off" value={joinName} onChange={(e) => setJoinName(e.target.value)} />
          </div>
          <div className="form-check">
            <input id="loy-join-consent" type="checkbox" className="form-check-input" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
            <label htmlFor="loy-join-consent" className="form-check-label">{t('cust.consent')}</label>
          </div>
          {error && <div className="invalid-feedback d-block">{error}</div>}
          <div className="d-flex gap-2">
            <button type="button" className="btn btn-primary btn-sm px-3" disabled={saving} onClick={join}>{t('loyalty.join')}</button>
            <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => { setJoining(false); setError(''); }}>{t('common.cancel')}</button>
          </div>
        </div>
      )}
    </div>
  );
}
