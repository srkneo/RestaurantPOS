import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useAuth } from '../AuthContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { formatMoney } from '../ui/format';
import './BillDialog.css';
import CreditNoteForm from './CreditNoteForm';
import { billWhatsAppUrl } from './billShare';

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
  const isApprover = roles.some((r) => VOID_ROLES.includes(r));  // managers and the owner need no PIN

  const [stage, setStage] = useState('loading'); // loading | create | pay | error
  const [bill, setBill] = useState(null);
  const [upiReady, setUpiReady] = useState(false);
  const [feedbackReady, setFeedbackReady] = useState(false);
  const [reprintNeedsPin, setReprintNeedsPin] = useState(true);
  const [feedbackLink, setFeedbackLink] = useState(null);
  const [discountMode, setDiscountMode] = useState('none'); // none | percent | amount
  const [discountValue, setDiscountValue] = useState('');
  const [method, setMethod] = useState('Cash');
  const [amount, setAmount] = useState('');
  const [tendered, setTendered] = useState('');
  const [reference, setReference] = useState('');
  const [voiding, setVoiding] = useState(false);
  const [voidReason, setVoidReason] = useState('');
  const [pin, setPin] = useState('');
  const [askReprint, setAskReprint] = useState(false);
  const [discountPin, setDiscountPin] = useState(false);
  const [crediting, setCrediting] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [tipOpen, setTipOpen] = useState(false);
  const [tipAmount, setTipAmount] = useState('');
  const [tipMethod, setTipMethod] = useState('Cash');
  const [tipWaiter, setTipWaiter] = useState('');

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
      setFeedbackReady(!!settings.feedbackReady);
      setReprintNeedsPin(settings.reprintNeedsPin !== false);
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

  // The bill's feedback page, fetched ahead so the WhatsApp button opens at once (pop-up blockers stop a late window.open).
  const billId = bill?.id;
  useEffect(() => {
    setFeedbackLink(null);
    if (!feedbackReady || billId == null) return undefined;
    let alive = true;
    fetch(`/api/bills/${billId}/feedback-link`)
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (alive && data?.token) setFeedbackLink(`${window.location.origin}/feedback/${data.token}`);
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, [feedbackReady, billId]);

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

  // A tip for the waiter: saved apart from the bill, so the bill total and GST stay the same.
  const saveTip = () => {
    const value = parseAmount(tipAmount);
    if (Number.isNaN(value) || value <= 0) {
      setError(t('tips.badAmount'));
      return;
    }
    run(async () => {
      const { res, data } = await postJson(`/api/bills/${bill.id}/tips`, { method: tipMethod, amount: value, waiterName: tipWaiter.trim() || null });
      if (res.ok) {
        const name = data.waiterName === 'Shared' ? t('tips.shared') : data.waiterName;
        showToast(t('tips.saved', { amount: formatMoney(data.amount), name }));
        setTipOpen(false);
        setTipAmount('');
        setTipWaiter('');
        onChanged?.();
      } else {
        setError(data.message || t('common.error'));
      }
    });
  };

  const createBill = () => {
    let body = {};
    if (discountMode !== 'none') {
      const value = parseAmount(discountValue);
      if (Number.isNaN(value) || value < 0) { setError(t('bill.badDiscount')); return; }
      body = discountMode === 'percent' ? { discountPercent: value } : { discountAmount: value };
    }
    if (discountPin && pin.trim()) body = { ...body, approverPin: pin.trim() };
    run(async () => {
      const { res, data } = await postJson(`/api/orders/${orderId}/bill`, body);
      if (res.ok) {
        showBill(data);
        onChanged?.();
      } else if (res.status === 409 && data.code === 'already-billed') {
        await load();
      } else if (res.status === 403 && (data.code === 'pin-required' || data.code === 'wrong-pin')) {
        // The discount is above what a cashier may give alone.
        setDiscountPin(true);
        setError(data.code === 'pin-required' ? t('approval.pinDiscount') : (data.message || t('common.error')));
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
    if (!isApprover && !pin.trim()) { setError(t('approval.pinRequired')); return; }
    run(async () => {
      const { res, data } = await postJson(`/api/bills/${bill.id}/void`,
        { reason: voidReason.trim(), version: bill.version, ...(isApprover ? {} : { approverPin: pin.trim() }) });
      if (res.ok) {
        showToast(t('bill.voidedToast', { number: bill.invoiceNumber }));
        setBill(null);
        setVoiding(false);
        setVoidReason('');
        setPin('');
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

  const openPrint = () => window.open(`/print/bill/${bill.id}?auto=1`, '_blank', 'noopener');

  // Printing a bill again needs a manager PIN when a cashier does it.
  const printBill = () => {
    if (Number(bill.printCount ?? 0) > 0 && !isApprover && reprintNeedsPin) { setAskReprint(true); setError(''); return; }
    openPrint();
  };

  const approveReprint = () => {
    if (!pin.trim()) { setError(t('approval.pinRequired')); return; }
    run(async () => {
      const { res, data } = await postJson('/api/approvals', { pin: pin.trim(), action: 'reprint', billId: bill.id });
      if (res.ok) {
        setAskReprint(false);
        setPin('');
        openPrint();
      } else {
        setError(data.message || t('common.error'));
      }
    });
  };

  const creditDone = (note) => {
    setCrediting(false);
    showToast(t('credit.createdToast', { number: note.number }));
    window.open(`/print/credit-note/${note.id}?auto=1`, '_blank', 'noopener');
    onChanged?.();
    load();
  };

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
            {discountPin && (
              <div>
                <label htmlFor="bill-discount-pin" className="form-label">{t('approval.pin')}</label>
                <input id="bill-discount-pin" type="text" inputMode="numeric" autoComplete="off" maxLength={6} className="form-control"
                  style={{ WebkitTextSecurity: 'disc' }} data-lpignore="true" data-1p-ignore="true" value={pin} onChange={(e) => setPin(e.target.value)} />
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

            {bill.status !== 'Void' && (tipOpen ? (
              <div className="pos-bill-tip d-flex flex-column gap-2">
                <div className="d-flex gap-2 flex-wrap">
                  <div className="flex-grow-1">
                    <label htmlFor="tip-amount" className="form-label">{t('tips.amount')}</label>
                    <input id="tip-amount" className="form-control" inputMode="decimal" autoComplete="off" value={tipAmount} onChange={(e) => setTipAmount(e.target.value)} />
                  </div>
                  <div className="flex-grow-1">
                    <label htmlFor="tip-method" className="form-label">{t('tips.method')}</label>
                    <select id="tip-method" className="form-select" value={tipMethod} onChange={(e) => setTipMethod(e.target.value)}>
                      {METHODS.map((m) => <option key={m} value={m}>{t(`bill.method.${m}`)}</option>)}
                    </select>
                  </div>
                </div>
                <div>
                  <label htmlFor="tip-waiter" className="form-label">{t('tips.waiter')}</label>
                  <input id="tip-waiter" className="form-control" maxLength={100} autoComplete="off" placeholder={t('tips.waiterHint')}
                    value={tipWaiter} onChange={(e) => setTipWaiter(e.target.value)} />
                </div>
                <button type="button" className="btn btn-outline-primary" disabled={busy} onClick={saveTip}>{t('tips.save')}</button>
              </div>
            ) : (
              <button type="button" className="btn btn-link px-0 align-self-start" onClick={() => setTipOpen(true)}>{t('tips.add')}</button>
            ))}

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
                <input id="bill-void-reason" className="form-control" autoComplete="off" maxLength={200} value={voidReason} onChange={(e) => setVoidReason(e.target.value)} />
                {!isApprover && (
                  <>
                    <label htmlFor="bill-void-pin" className="form-label mt-2">{t('approval.pin')}</label>
                    <input id="bill-void-pin" type="text" inputMode="numeric" autoComplete="off" maxLength={6} className="form-control" style={{ WebkitTextSecurity: 'disc' }} data-lpignore="true" data-1p-ignore="true"
                      value={pin} onChange={(e) => setPin(e.target.value)} />
                  </>
                )}
                <button type="button" className="btn btn-danger mt-2" disabled={busy} onClick={confirmVoid}>{t('bill.voidConfirm')}</button>
              </div>
            )}
          </>
        )}

        {askReprint && bill && (
          <div>
            <label htmlFor="bill-reprint-pin" className="form-label">{t('approval.pinReprint')}</label>
            <input id="bill-reprint-pin" type="text" inputMode="numeric" autoComplete="off" maxLength={6} className="form-control" style={{ WebkitTextSecurity: 'disc' }} data-lpignore="true" data-1p-ignore="true"
              value={pin} onChange={(e) => setPin(e.target.value)} />
            <button type="button" className="btn btn-primary mt-2" disabled={busy} onClick={approveReprint}>{t('approval.approve')}</button>
          </div>
        )}
        {crediting && bill && <CreditNoteForm bill={bill} needPin={!isApprover} onDone={creditDone} onCancel={() => setCrediting(false)} />}

        {error && <p className="text-danger mb-0" role="alert">{error}</p>}

        <div className="pos-dialog-actions mt-0">
          {stage === 'pay' && bill && !bill.creditedAt && !voiding && (
            <button type="button" className="btn btn-outline-danger" onClick={() => { setVoiding(true); setError(''); }}>{t('bill.void')}</button>
          )}
          {stage === 'pay' && bill && <button type="button" className="btn btn-outline-primary" onClick={printBill}>{t('bill.print')}</button>}
          {stage === 'pay' && bill && bill.status !== 'Void' && (
            <button type="button" className="btn btn-outline-success" onClick={() => window.open(billWhatsAppUrl(bill, t, feedbackLink), '_blank', 'noopener')}>{t('bill.whatsapp')}</button>
          )}
          {stage === 'pay' && bill && !bill.creditedAt && !crediting && (
            <button type="button" className="btn btn-outline-secondary" onClick={() => { setCrediting(true); setError(''); }}>{t('credit.button')}</button>
          )}
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>{t('common.close')}</button>
        </div>
      </div>
    </div>
  );
}
