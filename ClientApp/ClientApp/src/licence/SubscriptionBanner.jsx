import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { formatMoney } from '../ui/format';
import { loadAbout } from '../about/about';

const day = (iso, plusDays = 0) => {
  const d = new Date(`${iso}T00:00:00`);
  d.setDate(d.getDate() + plusDays);
  return d.toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
};

// Owner: a bar under the header in the last week of the trial, when a payment is due, and when the
// subscription has ended (new orders and bills stopped). Nothing for free use, shop PCs or a paid-up plan.
export default function SubscriptionBanner() {
  const t = useT();
  const [sub, setSub] = useState(null);
  const [contact, setContact] = useState(null);

  useEffect(() => {
    let alive = true;
    fetch('/api/subscription')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => { if (alive) setSub(data); })
      .catch(() => {});
    Promise.resolve(loadAbout()).then((data) => { if (alive) setContact(data); }).catch(() => {});
    return () => { alive = false; };
  }, []);

  if (!sub || !sub.paidUntil || sub.state === 'none' || sub.state === 'active') return null;
  if (sub.state === 'trial' && sub.daysLeft > 7) return null;

  const kind = sub.state === 'suspended' ? 'danger' : sub.state === 'overdue' ? 'warning' : 'info';
  const text = sub.state === 'trial' ? t('sub.trialEnds', { days: sub.daysLeft, date: day(sub.paidUntil) })
    : sub.state === 'overdue' ? t('sub.overdue', { date: day(sub.paidUntil, sub.graceDays + 1) })
      : t('sub.suspended');
  const phone = contact?.whatsApp || contact?.phone || '';
  return (
    <div className={`alert alert-${kind} rounded-0 mb-0 py-2`} role={kind === 'danger' ? 'alert' : 'status'}>
      <strong>{text}</strong>{' '}
      {t(phone ? 'sub.howToPay' : 'sub.howToPayNoPhone', { monthly: formatMoney(sub.monthly), yearly: formatMoney(sub.yearly), phone })}
    </div>
  );
}
