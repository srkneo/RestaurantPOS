import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';

const PRINTER_KEY = 'pos.setup.printerTested';

async function getJson(url) {
  try {
    const res = await fetch(url);
    return res.ok ? await res.json() : null;
  } catch {
    return null;
  }
}

function readPrinter() {
  try {
    return localStorage.getItem(PRINTER_KEY) === 'yes';
  } catch {
    return false;
  }
}

// Owner's setup checklist: what is done and what is left before the first day of billing. Read-only, with links.
export default function SetupPage() {
  const t = useT();
  const [data, setData] = useState(null);
  const [printer, setPrinter] = useState(readPrinter);

  const load = useCallback(async () => {
    const [store, items, tables, staff, billing, qr] = await Promise.all([
      getJson('/api/admin/store'), getJson('/api/admin/menu/items'), getJson('/api/admin/tables'), getJson('/api/staff'),
      getJson('/api/admin/billing-settings'), getJson('/api/admin/tables/qr-setting'),
    ]);
    setData({ store, items, tables, staff, billing, qr });
  }, []);

  useEffect(() => { load(); }, [load]);

  const markPrinter = () => {
    setPrinter(true);
    try {
      localStorage.setItem(PRINTER_KEY, 'yes');
    } catch {
      // This browser cannot remember it; the tick lasts until the page is closed.
    }
  };

  if (!data) return <StateMessage kind="loading" />;
  const steps = [
    { id: 'store', done: !!data.store?.restaurantName && !!data.store?.phone, to: '/admin/store' },
    { id: 'billing', done: !!data.billing && (data.billing.taxMode !== 'None' || !!data.billing.upiId), to: '/admin/billing' },
    { id: 'menu', done: (data.items?.length ?? 0) > 0, to: '/admin/menu' },
    { id: 'tables', done: (data.tables?.length ?? 0) > 0, to: '/admin/tables' },
    { id: 'staff', done: (data.staff?.length ?? 0) > 1, to: '/admin/staff' },
    { id: 'printer', done: printer, to: null },
    { id: 'qr', done: !!data.qr?.enabled, to: '/admin/tables/qr', optional: true },
  ];
  const left = steps.filter((s) => !s.done && !s.optional).length;

  return (
    <main className="pos-page">
      <div className="pos-page-head"><h1>{t('setup.title')}</h1></div>
      <p className="fw-semibold" role="status">{left === 0 ? t('setup.allDone') : t('setup.left', { count: left })}</p>
      <ol className="list-unstyled d-flex flex-column gap-2">
        {steps.map((s) => (
          <li key={s.id} className="pos-card p-3 d-flex flex-wrap align-items-center gap-3" aria-label={t(`setup.${s.id}`)}>
            <span className={`fw-bold ${s.done ? 'text-success' : 'text-body-secondary'}`} aria-hidden="true">{s.done ? '✓' : '○'}</span>
            <div className="flex-grow-1">
              <div className="fw-semibold">{t(`setup.${s.id}`)}{s.optional ? ` (${t('setup.optional')})` : ''}</div>
              <div className="pos-hint">{t(`setup.${s.id}Hint`)}</div>
            </div>
            <span className="visually-hidden">{s.done ? t('setup.done') : t('setup.todo')}</span>
            {s.to && <Link className="btn btn-outline-secondary btn-sm px-3" to={s.to}>{t('setup.open')}</Link>}
            {s.id === 'printer' && !s.done && (
              <button type="button" className="btn btn-outline-primary btn-sm px-3" onClick={markPrinter}>{t('setup.printerDone')}</button>
            )}
          </li>
        ))}
      </ol>
    </main>
  );
}
