import React, { useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { send } from '../ui/forms';

const METHODS = ['Cash', 'Upi', 'Card'];

export default function CreditNoteForm({ bill, needPin, onDone, onCancel }) {
    const t = useT();
    const [reason, setReason] = useState('');
    const [refundMethod, setRefundMethod] = useState('Cash');
    const [pin, setPin] = useState('');
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);

    const paid = Number(bill.amountPaid ?? 0) > 0;

    async function submit() {
        if (!reason.trim()) {
            setError(t('credit.reasonRequired'));
            return;
        }
        if (needPin && !pin.trim()) {
            setError(t('approval.pinRequired'));
            return;
        }

        setBusy(true);
        setError('');
        const res = await send(`/api/bills/${bill.id}/credit-note`, 'POST', {
            reason: reason.trim(),
            refundMethod: paid ? refundMethod : null,
            approverPin: needPin ? pin.trim() : null,
            version: bill.version
        });
        const data = await res.json().catch(() => ({}));
        setBusy(false);

        if (res.ok) {
            onDone?.(data);
        } else {
            setError(data.message || t('common.error'));
        }
    }

    return (
        <div className="d-flex flex-column gap-2 border rounded p-3">
            <strong>{t('credit.title')}</strong>
            <label htmlFor="credit-reason" className="form-label mb-0">{t('credit.reason')}</label>
            <input id="credit-reason" className="form-control" maxLength={200} value={reason} onChange={(e) => setReason(e.target.value)} />
            {paid && (
                <>
                    <label htmlFor="credit-refund" className="form-label mb-0">{t('credit.refundMethod')}</label>
                    <select id="credit-refund" className="form-select" value={refundMethod} onChange={(e) => setRefundMethod(e.target.value)}>
                        {METHODS.map((m) => (
                            <option key={m} value={m}>{t(`bill.method.${m}`)}</option>
                        ))}
                    </select>
                </>
            )}
            {needPin && (
                <>
                    <label htmlFor="credit-pin" className="form-label mb-0">{t('approval.pin')}</label>
                    <input id="credit-pin" type="password" inputMode="numeric" autoComplete="off" maxLength={6} className="form-control" value={pin} onChange={(e) => setPin(e.target.value)} />
                </>
            )}
            {error && <p className="text-danger mb-0">{error}</p>}
            <div className="d-flex gap-2">
                <button type="button" className="btn btn-danger" disabled={busy} onClick={submit}>{t('credit.create')}</button>
                <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>{t('common.cancel')}</button>
            </div>
        </div>
    );
}
