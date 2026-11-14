import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { readErrors, send, Field } from '../ui/forms';
import '../components/OrderHistory.css';
import './StaffPage.css';

export default function SettingsPage() {
    const t = useT();
    const { showToast } = useToast();

    const PRESETS = ['dine-in', 'cafe', 'takeaway-counter'];

    const [form, setForm] = useState(null);
    const [loadState, setLoadState] = useState('loading');
    const [errors, setErrors] = useState({});
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        try {
            const res = await fetch('/api/admin/settings');
            if (res.ok) {
                setForm(await res.json());
                setLoadState('ready');
            } else {
                setLoadState('error');
            }
        } catch (error) {
            setLoadState('error');
        }
    }, []);

    useEffect(() => {
        load();
    }, [load]);

    const check = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.checked }));
    const text = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));

    const checkbox = (key, id, label) => (
        <div className="pos-check" key={id}>
            <input id={id} type="checkbox" className="form-check-input" checked={!!form[key]} onChange={check(key)} />
            <label htmlFor={id} className="mb-0 fw-semibold">{t(label)}</label>
        </div>
    );

    const applyPreset = async (preset) => {
        const res = await fetch('/api/admin/settings/preset', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ preset })
        });
        if (res.ok) {
            setForm(await res.json());
            setErrors({});
            showToast(t('settings.presetApplied'));
        } else {
            setErrors(await readErrors(res));
        }
    };

    const save = async (e) => {
        e.preventDefault();
        const body = {
            ...form,
            firstToken: Number(form.firstToken),
            businessDayStartHour: Number(form.businessDayStartHour),
            maxItemQuantity: Number(form.maxItemQuantity)
        };
        setBusy(true);
        const res = await fetch('/api/admin/settings', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            setErrors(await readErrors(res));
        } else {
            setForm(await res.json());
            setErrors({});
            showToast(t('settings.saved'));
        }
        setBusy(false);
    };

    return (
        <main className="pos-page">
            <div className="pos-page-head">
                <h1>{t('settings.title')}</h1>
            </div>
            {loadState === 'loading' && <StateMessage kind="loading" />}
            {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
            {loadState === 'ready' && (
                <>
                    <section className="pos-card p-3 d-flex flex-column gap-2">
                        <h2 className="h5 mb-0">{t('settings.presets')}</h2>
                        <p className="pos-hint mb-0">{t('settings.presetsHint')}</p>
                        <div className="d-flex flex-wrap gap-2">
                            {PRESETS.map(preset => (
                                <button
                                    key={preset}
                                    type="button"
                                    className="btn btn-outline-secondary px-3"
                                    onClick={() => applyPreset(preset)}
                                >
                                    {t(`settings.preset.${preset}`)}
                                </button>
                            ))}
                        </div>
                    </section>
                    <form className="pos-card p-3 d-flex flex-column gap-3" noValidate onSubmit={save}>
                        <h2 className="h5 mb-0">{t('settings.orderTypes')}</h2>
                        {checkbox('dineInEnabled', 'set-dinein', 'settings.dineInEnabled')}
                        {checkbox('takeawayEnabled', 'set-takeaway', 'settings.takeawayEnabled')}
                        {errors.ordertypes && <div className="invalid-feedback d-block">{errors.ordertypes}</div>}
                        <Field
                            id="set-default"
                            label={t('settings.defaultOrderType')}
                            error={errors.defaultordertype}
                        >
                            <select
                                id="set-default"
                                className="form-select"
                                value={form.defaultOrderType}
                                onChange={text('defaultOrderType')}
                            >
                                <option value="DineIn">{t('pos.dineIn')}</option>
                                <option value="Takeaway">{t('pos.takeaway')}</option>
                            </select>
                        </Field>
                        {checkbox('takeawayNeedsName', 'set-needname', 'settings.takeawayNeedsName')}
                        {checkbox('kitchenScreenEnabled', 'set-kitchen', 'settings.kitchenScreenEnabled')}
                        <h2 className="h5 mb-0">{t('settings.tokens')}</h2>
                        <Field
                            id="set-firsttoken"
                            label={t('settings.firstToken')}
                            error={errors.firsttoken}
                        >
                            <input
                                id="set-firsttoken"
                                type="number"
                                className="form-control"
                                value={form.firstToken}
                                onChange={text('firstToken')}
                            />
                        </Field>
                        <Field
                            id="set-dayhour"
                            label={t('settings.businessDayStartHour')}
                            error={errors.businessdaystarthour}
                        >
                            <input
                                id="set-dayhour"
                                type="number"
                                className="form-control"
                                value={form.businessDayStartHour}
                                onChange={text('businessDayStartHour')}
                            />
                        </Field>
                        <Field
                            id="set-maxqty"
                            label={t('settings.maxItemQuantity')}
                            error={errors.maxitemquantity}
                        >
                            <input
                                id="set-maxqty"
                                type="number"
                                className="form-control"
                                value={form.maxItemQuantity}
                                onChange={text('maxItemQuantity')}
                            />
                        </Field>
                        <Field
                            id="set-zone"
                            label={t('settings.timeZone')}
                            error={errors.timezone}
                        >
                            <input
                                id="set-zone"
                                className="form-control"
                                value={form.timeZone}
                                onChange={text('timeZone')}
                            />
                        </Field>
                        {errors.general !== undefined && (
                            <p className="text-danger mb-0" role="alert">{errors.general || t('common.error')}</p>
                        )}
                        <button
                            type="submit"
                            className="btn btn-primary px-4 align-self-start"
                            disabled={busy}
                        >
                            {t('settings.save')}
                        </button>
                    </form>
                </>
            )}
        </main>
    );
}
