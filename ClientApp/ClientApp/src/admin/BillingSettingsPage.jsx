import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { readErrors, send, Field } from '../ui/forms';
import '../components/OrderHistory.css';
import './StaffPage.css';

const TAX_MODES = ['None', 'Regular', 'Composition'];

export default function BillingSettingsPage() {
    const t = useT();
    const { showToast } = useToast();

    const [form, setForm] = useState(null);
    const [loadState, setLoadState] = useState('loading');
    const [errors, setErrors] = useState({});
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        setLoadState('loading');
        try {
            const res = await fetch('/api/admin/billing-settings');
            if (res.ok) {
                const data = await res.json();
                setForm({
                    ...data,
                    billPrefix: data.billPrefix ?? '',
                    upiId: data.upiId ?? '',
                    upiName: data.upiName ?? '',
                    paperWidth: String(data.paperWidth),
                    printLanguage: data.printLanguage
                });
                setLoadState('ready');
            } else {
                setLoadState('error');
            }
        } catch {
            setLoadState('error');
        }
    }, []);

    useEffect(() => {
        load();
    }, [load]);

    const setField = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));

    const save = async (e) => {
        e.preventDefault();
        const body = {
            taxMode: form.taxMode,
            pricesIncludeTax: !!form.pricesIncludeTax,
            billPrefix: form.billPrefix.trim().toUpperCase(),
            upiId: form.upiId.trim() === '' ? null : form.upiId.trim(),
            upiName: form.upiName.trim() === '' ? null : form.upiName.trim(),
            paperWidth: Number(form.paperWidth),
            printLanguage: form.printLanguage,
        };
        setBusy(true);
        try {
            const res = await send('/api/admin/billing-settings', 'PUT', body);
            if (res.ok) {
                setErrors({});
                showToast(t('billing.saved'));
            } else {
                setErrors(await readErrors(res));
            }
        } finally {
            setBusy(false);
        }
    };

    return (
        <main className="pos-page">
            <div className="pos-page-head">
                <h1>{t('billing.title')}</h1>
            </div>
            {loadState === 'loading' && <StateMessage kind="loading" />}
            {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
            {loadState === 'ready' && form && (
                <form className="pos-card d-flex flex-column gap-3" style={{ maxWidth: 560 }} onSubmit={save} noValidate>
                    <Field
                        id="billing-tax"
                        label={t('billing.taxMode')}
                        error={errors.taxmode}
                        hint={form.taxMode !== 'None' ? t('billing.gstinHint') : null}
                    >
                        <select
                            id="billing-tax"
                            className="form-select"
                            value={form.taxMode}
                            onChange={setField('taxMode')}
                        >
                            {TAX_MODES.map((m) => (
                                <option key={m} value={m}>
                                    {t(`billing.taxMode.${m}`)}
                                </option>
                            ))}
                        </select>
                    </Field>
                    <div className="pos-check">
                        <input
                            id="billing-inclusive"
                            type="checkbox"
                            className="form-check-input"
                            checked={!!form.pricesIncludeTax}
                            onChange={(e) => setForm((f) => ({ ...f, pricesIncludeTax: e.target.checked }))}
                        />
                        <label htmlFor="billing-inclusive" className="mb-0 fw-semibold">
                            {t('billing.pricesIncludeTax')}
                        </label>
                    </div>
                    <Field
                        id="billing-prefix"
                        label={t('billing.billPrefix')}
                        hint={t('billing.billPrefixHint')}
                        error={errors.billprefix}
                    >
                        <input
                            id="billing-prefix"
                            className="form-control"
                            maxLength={3}
                            value={form.billPrefix}
                            onChange={setField('billPrefix')}
                        />
                    </Field>
                    <Field
                        id="billing-upi"
                        label={t('billing.upiId')}
                        hint={t('billing.upiIdHint')}
                        error={errors.upiid}
                    >
                        <input
                            id="billing-upi"
                            className="form-control"
                            maxLength={100}
                            autoComplete="off"
                            value={form.upiId}
                            onChange={setField('upiId')}
                        />
                    </Field>
                    <Field
                        id="billing-upi-name"
                        label={t('billing.upiName')}
                        error={errors.upiname}
                    >
                        <input
                            id="billing-upi-name"
                            className="form-control"
                            maxLength={50}
                            value={form.upiName}
                            onChange={setField('upiName')}
                        />
                    </Field>
                    <Field
                        id="billing-paper"
                        label={t('billing.paperWidth')}
                        error={errors.paperwidth}
                    >
                        <select
                            id="billing-paper"
                            className="form-select"
                            value={form.paperWidth}
                            onChange={setField('paperWidth')}
                        >
                            <option value="80">{t('billing.paper.80')}</option>
                            <option value="58">{t('billing.paper.58')}</option>
                        </select>
                    </Field>
                    <Field
                        id="billing-lang"
                        label={t('billing.printLanguage')}
                        error={errors.printlanguage}
                    >
                        <select
                            id="billing-lang"
                            className="form-select"
                            value={form.printLanguage}
                            onChange={setField('printLanguage')}
                        >
                            <option value="en">{t('billing.lang.en')}</option>
                            <option value="gu">{t('billing.lang.gu')}</option>
                        </select>
                    </Field>
                    {errors.general !== undefined && (
                        <p className="text-danger mb-0" role="alert">
                            {errors.general || t('common.error')}
                        </p>
                    )}
                    <div>
                        <button
                            type="submit"
                            className="btn btn-primary px-4"
                            disabled={busy}
                        >
                            {t('billing.save')}
                        </button>
                    </div>
                </form>
            )}
        </main>
    );
}
