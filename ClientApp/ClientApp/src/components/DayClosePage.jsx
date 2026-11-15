import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { useAuth } from '../AuthContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { send } from '../ui/forms';
import { formatMoney } from '../ui/format';
import './OrderHistory.css';
import './DayClosePage.css';

const isAmount = (value) => value.trim() !== '' && Number.isFinite(Number(value)) && Number(value) >= 0;

async function messageOf(res, fallback) {
  try {
    const data = await res.json();
    return data?.message || data?.title || fallback;
  } catch {
    return fallback;
  }
}

function Summary({ shift, t }) {
  const rows = [
    ['dayClose.openingCash', shift.openingCash],
    ['dayClose.cashSales', shift.cashSales],
    ['dayClose.cashIn', shift.cashIn],
    ['dayClose.cashOut', shift.cashOut],
    ['dayClose.expectedCash', shift.expectedCash],
  ];
  if (shift.countedCash !== null && shift.countedCash !== undefined) rows.push(['dayClose.countedCash', shift.countedCash]);
  return (
    <dl className="day-summary">
      {rows.map(([key, value]) => (
        <div key={key} className={key === 'dayClose.expectedCash' ? 'day-strong' : undefined}>
          <dt>{t(key)}</dt><dd>{formatMoney(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

// Till shift for the cashier: open with the cash in the drawer, record cash in/out, close with the cash counted.
export default function DayClosePage() {
  const t = useT();
  const user = useAuth()?.user;
  const { showToast } = useToast();
  const [loadState, setLoadState] = useState('loading');
  const [shift, setShift] = useState(null);
  const [closed, setClosed] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [opening, setOpening] = useState('');
  const [move, setMove] = useState({ kind: 'Out', amount: '', reason: '' });
  const [counted, setCounted] = useState('');
  const [note, setNote] = useState('');
  const canReport = (user?.roles ?? []).some((r) => r === 'manager' || r === 'admin');

  const load = useCallback(async () => {
    setLoadState('loading');
    try {
      const res = await fetch('/api/shifts/current');
      if (!res.ok) {
        setLoadState('error');
        return;
      }
      const data = await res.json();
      setShift(data.shift ?? null);
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const post = async (url, body, onOk) => {
    setBusy(true);
    setError('');
    try {
      const res = await send(url, 'POST', body);
      if (res.ok) {
        onOk(await res.json());
      } else {
        setError(await messageOf(res, t('common.error')));
        if (res.status === 409) await load();
      }
    } catch {
      setError(t('common.error'));
    } finally {
      setBusy(false);
    }
  };

  const openShift = (e) => {
    e.preventDefault();
    if (!isAmount(opening)) { setError(t('dayClose.enterAmount')); return; }
    post('/api/shifts/open', { openingCash: Number(opening) }, (s) => {
      setShift(s);
      setClosed(null);
      setOpening('');
      showToast(t('dayClose.openedToast'));
    });
  };

  const addMove = (e) => {
    e.preventDefault();
    if (!isAmount(move.amount) || Number(move.amount) <= 0) { setError(t('dayClose.enterAmount')); return; }
    if (!move.reason.trim()) { setError(t('dayClose.reasonRequired')); return; }
    post('/api/shifts/current/movements', { kind: move.kind, amount: Number(move.amount), reason: move.reason.trim() }, (s) => {
      setShift(s);
      setMove((m) => ({ ...m, amount: '', reason: '' }));
    });
  };

  const closeShift = (e) => {
    e.preventDefault();
    if (!isAmount(counted)) { setError(t('dayClose.enterAmount')); return; }
    post('/api/shifts/current/close', { countedCash: Number(counted), note: note.trim() || null, version: shift.version }, (s) => {
      setClosed(s);
      setShift(null);
      setCounted('');
      setNote('');
      showToast(t('dayClose.closedToast'));
    });
  };

  const differenceText = (d) => {
    if (d < 0) return t('dayClose.short', { amount: formatMoney(-d) });
    if (d > 0) return t('dayClose.excess', { amount: formatMoney(d) });
    return t('dayClose.matched');
  };
  const preview = shift && isAmount(counted) ? Number(counted) - Number(shift.expectedCash) : null;

  return (
    <main className="pos-page day-close">
      <div className="pos-page-head">
        <h1>{t('dayClose.title')}</h1>
        {canReport && <Link className="btn btn-outline-secondary" to="/reports/day">{t('dayClose.dayReport')}</Link>}
      </div>
      {error && <p className="text-danger mb-0" role="alert">{error}</p>}
      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}

      {loadState === 'ready' && closed && (
        <section className="pos-card day-card" aria-label={t('dayClose.closedTitle')}>
          <h2>{t('dayClose.closedTitle')}</h2>
          <Summary shift={closed} t={t} />
          <p className={`day-diff ${Number(closed.difference) < 0 ? 'text-danger' : ''}`}>{differenceText(Number(closed.difference))}</p>
        </section>
      )}

      {loadState === 'ready' && !shift && (
        <form className="pos-card day-card" onSubmit={openShift} noValidate>
          <h2>{t('dayClose.noShift')}</h2>
          <label htmlFor="day-opening" className="form-label fw-semibold">{t('dayClose.openingCash')}</label>
          <input id="day-opening" className="form-control" inputMode="decimal" value={opening} onChange={(e) => setOpening(e.target.value)} />
          <p className="pos-hint">{t('dayClose.openingHint')}</p>
          <div><button type="submit" className="btn btn-primary px-4" disabled={busy}>{t('dayClose.open')}</button></div>
        </form>
      )}

      {loadState === 'ready' && shift && (
        <>
          <section className="pos-card day-card" aria-label={t('dayClose.current')}>
            <h2>{t('dayClose.current')}</h2>
            <Summary shift={shift} t={t} />
            {shift.movements.length > 0 && (
              <ul className="day-moves">
                {shift.movements.map((m) => (
                  <li key={m.id}>{t(`dayClose.kind.${m.kind}`)} {formatMoney(m.amount)} – {m.reason}</li>
                ))}
              </ul>
            )}
          </section>

          <form className="pos-card day-card" onSubmit={addMove} noValidate aria-label={t('dayClose.moveTitle')}>
            <h2>{t('dayClose.moveTitle')}</h2>
            <div className="day-move-row">
              <div>
                <label htmlFor="day-kind" className="form-label fw-semibold">{t('dayClose.kind')}</label>
                <select id="day-kind" className="form-select" value={move.kind} onChange={(e) => setMove((m) => ({ ...m, kind: e.target.value }))}>
                  <option value="Out">{t('dayClose.kind.Out')}</option>
                  <option value="In">{t('dayClose.kind.In')}</option>
                </select>
              </div>
              <div>
                <label htmlFor="day-move-amount" className="form-label fw-semibold">{t('dayClose.amount')}</label>
                <input id="day-move-amount" className="form-control" inputMode="decimal" value={move.amount}
                  onChange={(e) => setMove((m) => ({ ...m, amount: e.target.value }))} />
              </div>
              <div className="day-grow">
                <label htmlFor="day-move-reason" className="form-label fw-semibold">{t('dayClose.reason')}</label>
                <input id="day-move-reason" className="form-control" maxLength={100} value={move.reason}
                  onChange={(e) => setMove((m) => ({ ...m, reason: e.target.value }))} />
              </div>
            </div>
            <div><button type="submit" className="btn btn-outline-primary px-4" disabled={busy}>{t('dayClose.saveMove')}</button></div>
          </form>

          <form className="pos-card day-card" onSubmit={closeShift} noValidate aria-label={t('dayClose.closeTitle')}>
            <h2>{t('dayClose.closeTitle')}</h2>
            <label htmlFor="day-counted" className="form-label fw-semibold">{t('dayClose.countedCash')}</label>
            <input id="day-counted" className="form-control" inputMode="decimal" value={counted} onChange={(e) => setCounted(e.target.value)} />
            {preview !== null && <p className={`day-diff ${preview < 0 ? 'text-danger' : ''}`} data-testid="day-preview">{differenceText(preview)}</p>}
            <label htmlFor="day-note" className="form-label fw-semibold">{t('dayClose.note')}</label>
            <input id="day-note" className="form-control" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} />
            <div><button type="submit" className="btn btn-primary px-4" disabled={busy}>{t('dayClose.close')}</button></div>
          </form>
        </>
      )}
    </main>
  );
}
