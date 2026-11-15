import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useAuth } from '../AuthContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { formatMoney } from '../ui/format';
import './BillDialog.css';

export const METHODS = ['Cash', 'Upi', 'Card'];
const VOID_ROLES = ['manager', 'admin'];

// '₹1,250.50' -> 1250.5; empty or not a number -> NaN.
export function parseAmount(text) {
  const clean = String(text ?? '').replace(/[₹,\s]/g, '');
  if (clean === '') return NaN;
  const n = Number(clean);
  return Number.isFinite(n) ? Math.round(n * 100) / 100 : NaN;
}

async function postJson(url, body) {
  const res = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) });
  const data = await res.json().catch(() => ({}));
  return { res, data };
}

// Bill for one order: make it (with an optional discount), take cash / UPI / card (split allowed), print, void.
export default function BillDialog({ orderId, onClose, onChanged }) {
  const t = useT();
  const auth = useAuth();
  const { showToast } = useToast();
  const roles = auth?.user?.roles ?? [];

  const [stage, setStage] = useState('loading'); // loading | create | pay | error
  const [bill, setBill] = useState(null);
  const [upiReady, setUpiReady] = useState(false);
  const [discountMode, setDiscountMode] = useState('none'); // none | percent | amount
  const [discountValue, setDiscountValue] = useState('');
  const [method, setMethod] = useState('Cash');
  const [amount, setAmount] = useState('');
  const [tendered, setTendered] = useState('');
  const [reference, setReference] = useState('');
  const [voiding, setVoiding] = useState(false);
  const [voidReason, setVoidReason] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const showBill = useCallback((b) => {
    setBill(b);
    setStage('pay');
    setAmount(String(b.amountDue));
    setTendered('');
    setReference('');
    setVoiding(false);
    setError('');
  }, []);

  const load = useCallback(async () => {
    setStage('loading');
    setError('');
    try {
      const [billRes, settingsRes] = await Promise.all([fetch(`/api/orders/${orderId}/bill`), fetch('/api/print-settings')]);
      const settings = settingsRes.ok ? await settingsRes.json() : {};
      setUpiReady(!!settings.upiReady);
      if (billRes.status === 404) {
        setBill(null);
        setStage('create');
      } else if (billRes.ok) {
        showBill(await billRes.json());
      } else {
        setStage('error');
      }
    } catch {
      setStage('error');
    }
  }, [orderId, showBill]);

  useEffect(() => {
    if (orderId == null) return;
    setDiscountMode('none');
    setDiscountValue('');
    setMethod('Cash');
    load();
  }, [orderId, load]);

  useEffect(() => {
    if (orderId == null) return undefined;
    const onKey = (e) => { if (e.key === 'Escape') onClose?.(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [orderId, onClose]);

  if (orderId == null) return null;

  const run = async (action) => {
    setBusy(true);
    setError('');
    try {
      await action();
    } catch {
      setError(t('pos.networkError'));
    } finally {
      setBusy(false);
    }
  };

  const createBill = () => {
    let body = {};
    if (discountMode !== 'none') {
      const value = parseAmount(discountValue);
      if (Number.isNaN(value) || value < 0) { setError(t('bill.badDiscount')); return; }
      body = discountMode === 'percent' ? { discountPercent: value } : { discountAmount: value };
    }
    run(async () => {
      const { res, data } = await postJson(`/api/orders/${orderId}/bill`, body);
      if (res.ok) {
        showBill(data);
        onChanged?.();
      } else if (res.status === 409 && data.code === 'already-billed') {
        await load();
      } else {
        setError(data.message || t('common.error'));
      }
    });
  };

  const due = bill ? Number(bill.amountDue) : 0;
  const amountValue = parseAmount(amount);
  const tenderedValue = parseAmount(tendered);
  const change = method === 'Cash' && !Number.isNaN(tenderedValue) && !Number.isNaN(amountValue) && tenderedValue >= amountValue
    ? tenderedValue - amountValue : null;

  const pay = () => {
    if (Number.isNaN(amountValue) || amountValue <= 0) { setError(t('bill.enterAmount')); return; }
    if (amountValue > due) { setError(t('bill.tooMuch', { due: formatMoney(due) })); return; }
    const cashGiven = method === 'Cash' && tendered.trim() !== '';
    if (cashGiven && (Number.isNaN(tenderedValue) || tenderedValue < amountValue)) { setError(t('bill.cashTooLow')); return; }
    run(async () => {
      const { res, data } = await postJson(`/api/bills/${bill.id}/payments`, {
        method,
        amount: amountValue,
        tendered: cashGiven ? tenderedValue : null,
        reference: method !== 'Cash' && reference.trim() ? reference.trim() : null,
        version: bill.version,
      });
      if (res.ok) {
        showBill(data);
        onChanged?.();
        if (data.status === 'Paid') showToast(t('bill.paidToast', { number: data.invoiceNumber }));
      } else if (res.status === 409) {
        setError(t('pos.conflict'));
        await load();
      } else {
        setError(data.message || t('common.error'));
      }
    });
  };

  const confirmVoid = () => {
    if (!voidReason.trim()) { setError(t('bill.voidReasonRequired')); return; }
    run(async () => {
      const { res, data } = await postJson(`/api/bills/${bill.id}/void`, { reason: voidReason.trim(), version: bill.version });
      if (res.ok) {
        showToast(t('bill.voidedToast', { number: bill.invoiceNumber }));
        setBill(null);
        setVoiding(false);
        setVoidReason('');
        setStage('create');
        onChanged?.();
      } else if (res.status === 409) {
        setError(t('pos.conflict'));
        await load();
      } else {
        setError(data.message || t('common.error'));
      }
    });
  };

  const printBill = () => window.open(`/print/bill/${bill.id}?auto=1`, '_blank', 'noopener');

  return (
    <div className="pos-overlay">
      <div className="pos-dialog pos-bill-dialog d-flex flex-column gap-3" role="dialog" aria-modal="true" aria-labelledby="bill-title">
        <h2 id="bill-title">{bill ? t('bill.titleNumber', { number: bill.invoiceNumber }) : t('bill.title')}</h2>

        {stage === 'loading' && <StateMessage kind="loading" />}
        {stage === 'error' && <StateMessage kind="error" onRetry={load} />}

        {stage === 'create' && (
          <>
            <fieldset>
              <legend className="form-label">{t('bill.discount')}</legend>
              <div className="pos-bill-choices">
                {['none', 'percent', 'amount'].map((mode) => (
                  <label key={mode} className="pos-bill-choice">
                    <input type="radio" name="bill-discount-mode" checked={discountMode === mode} onChange={() => setDiscountMode(mode)} />
                    {t(`bill.discount.${mode}`)}
                  </label>
                ))}
              </div>
            </fieldset>
            {discountMode !== 'none' && (
              <div>
                <label htmlFor="bill-discount" className="form-label">{t(`bill.discountValue.${discountMode}`)}</label>
                <input id="bill-discount" className="form-control" inputMode="decimal" value={discountValue} onChange={(e) => setDiscountValue(e.target.value)} />
              </div>
            )}
            <button type="button" className="btn btn-primary pos-bill-main" disabled={busy} onClick={createBill}>{t('bill.make')}</button>
          </>
        )}

        {stage === 'pay' && bill && (
          <>
            <div className="pos-bill-summary">
              <div><span>{t('bill.total')}</span><strong>{formatMoney(bill.total)}</strong></div>
              {Number(bill.discount) > 0 && <div><span>{t('bill.discountGiven')}</span><span>{formatMoney(bill.discount)}</span></div>}
              <div><span>{t('bill.paidSoFar')}</span><span>{formatMoney(bill.amountPaid)}</span></div>
              <div><span>{t('bill.due')}</span><strong>{formatMoney(due)}</strong></div>
            </div>

            {(bill.payments ?? []).length > 0 && (
              <ul className="pos-bill-payments">
                {bill.payments.map((p) => (
                  <li key={p.id}>{t(`bill.method.${p.method}`)} {formatMoney(p.amount)}{p.change ? ` (${t('bill.changeGiven', { amount: formatMoney(p.change) })})` : ''}</li>
                ))}
              </ul>
            )}

            {bill.status === 'Paid' && <p className="pos-bill-paid" role="status">{t('bill.paid')}</p>}

            {bill.status === 'Unpaid' && due > 0 && (
              <>
                <div className="pos-bill-choices" role="group" aria-label={t('bill.method')}>
                  {METHODS.map((m) => (
                    <button key={m} type="button" className="pos-chip" aria-pressed={method === m} onClick={() => setMethod(m)}>{t(`bill.method.${m}`)}</button>
                  ))}
                </div>
                <div>
                  <label htmlFor="bill-amount" className="form-label">{t('bill.amount')}</label>
                  <input id="bill-amount" className="form-control" autoComplete="off" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
                </div>
                {method === 'Cash' && (
                  <div>
                    <label htmlFor="bill-tendered" className="form-label">{t('bill.tendered')}</label>
                    <input id="bill-tendered" className="form-control" autoComplete="off" inputMode="decimal" value={tendered} onChange={(e) => setTendered(e.target.value)} />
                    {change !== null && <p className="pos-bill-change mb-0">{t('bill.change', { amount: formatMoney(change) })}</p>}
                  </div>
                )}
                {method === 'Upi' && (upiReady
                  ? <img className="pos-bill-qr" src={`/api/bills/${bill.id}/upi-qr.svg?due=${due}`} alt={t('bill.scanQr')} />
                  : <p className="pos-hint mb-0">{t('bill.noUpi')}</p>)}
                {method !== 'Cash' && (
                  <div>
                    <label htmlFor="bill-reference" className="form-label">{t('bill.reference')}</label>
                    <input id="bill-reference" className="form-control" autoComplete="off" maxLength={40} value={reference} onChange={(e) => setReference(e.target.value)} />
                  </div>
                )}
                <button type="button" className="btn btn-primary pos-bill-main" disabled={busy} onClick={pay}>{t('bill.record')}</button>
              </>
            )}

            {voiding && (
              <div>
                <label htmlFor="bill-void-reason" className="form-label">{t('bill.voidReason')}</label>
                <input id="bill-void-reason" className="form-control" maxLength={200} value={voidReason} onChange={(e) => setVoidReason(e.target.value)} />
                <button type="button" className="btn btn-danger mt-2" disabled={busy} onClick={confirmVoid}>{t('bill.voidConfirm')}</button>
              </div>
            )}
          </>
        )}

        {error && <p className="text-danger mb-0" role="alert">{error}</p>}

        <div className="pos-dialog-actions mt-0">
          {stage === 'pay' && bill && roles.some((r) => VOID_ROLES.includes(r)) && !voiding && (
            <button type="button" className="btn btn-outline-danger" onClick={() => { setVoiding(true); setError(''); }}>{t('bill.void')}</button>
          )}
          {stage === 'pay' && bill && <button type="button" className="btn btn-outline-primary" onClick={printBill}>{t('bill.print')}</button>}
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>{t('common.close')}</button>
        </div>
      </div>
    </div>
  );
}
