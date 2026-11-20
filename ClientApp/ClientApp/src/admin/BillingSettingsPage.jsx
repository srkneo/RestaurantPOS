import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import DiscountLimitCard from './DiscountLimitCard';
import ApprovalRulesCard from './ApprovalRulesCard';
import { readErrors, send, Field } from '../ui/forms';
import ConfirmDialog from '../ui/ConfirmDialog';
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
    // GST changes every bill from now on, so a change of the GST mode is asked first.
    const [savedTaxMode, setSavedTaxMode] = useState(null);
    const [askTax, setAskTax] = useState(false);

    const load = useCallback(async () => {
        setLoadState('loading');
        try {
            const res = await fetch('/api/admin/billing-settings');
            if (res.ok) {
                const data = await res.json();
                setSavedTaxMode(data.taxMode);
                setForm({
                    ...data,
                    billPrefix: data.billPrefix ?? '',
                    upiId: data.upiId ?? '',
                    upiName: data.upiName ?? '',
                    paperWidth: String(data.paperWidth),
                    printLanguage: data.printLanguage,
                    smallChangeLimit: data.smallChangeLimit == null ? '' : String(data.smallChangeLimit)
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

    const save = (e) => {
        e.preventDefault();
        if (savedTaxMode !== null && form.taxMode !== savedTaxMode) { setAskTax(true); return; }
        doSave();
    };

    const doSave = async () => {
        setAskTax(false);
        const body = {
            taxMode: form.taxMode,
            pricesIncludeTax: !!form.pricesIncludeTax,
            billPrefix: form.billPrefix.trim().toUpperCase(),
            upiId: form.upiId.trim() === '' ? null : form.upiId.trim(),
            upiName: form.upiName.trim() === '' ? null : form.upiName.trim(),
            paperWidth: Number(form.paperWidth),
            printLanguage: form.printLanguage,
            ...(form.smallChangeLimit !== '' && form.smallChangeLimit != null ? { smallChangeLimit: Number(form.smallChangeLimit) } : {}),
        };
        setBusy(true);
        try {
            const res = await send('/api/admin/billing-settings', 'PUT', body);
            if (res.ok) {
                setErrors({});
                setSavedTaxMode(body.taxMode);
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
                            <option value="hi">{t('billing.lang.hi')}</option>
                        </select>
                    </Field>
                    {form.smallChangeLimit !== '' && form.smallChangeLimit != null && (
                        <Field id="billing-small-change" label={t('billing.smallChange')} hint={t('billing.smallChangeHint')} error={errors.smallchangelimit}>
                            <input id="billing-small-change" type="number" min={0} max={20} step={1} className="form-control"
                                value={form.smallChangeLimit} onChange={setField('smallChangeLimit')} />
                        </Field>
                    )}
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
            {loadState === 'ready' && <DiscountLimitCard />}
            {loadState === 'ready' && <ApprovalRulesCard />}
            <ConfirmDialog open={askTax} title={t('confirm.taxTitle')} message={t('confirm.taxText')} confirmLabel={t('confirm.taxYes')}
                onCancel={() => setAskTax(false)} onConfirm={doSave} />
        </main>
    );
}
