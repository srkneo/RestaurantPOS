import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { readErrors, send, Field } from '../ui/forms';
import ConfirmDialog from '../ui/ConfirmDialog';
import '../components/OrderHistory.css';
import './StaffPage.css';

export default function SettingsPage() {
    const t = useT();
    const { showToast } = useToast();

    const PRESETS = ['dine-in', 'cafe', 'takeaway-counter', 'bakery'];
    // Common time zones for the outlets we sell to, India first. A saved zone that is not listed is still shown.
    const TIME_ZONES = [
        ['Asia/Kolkata', 'India (UTC+05:30)'],
        ['Asia/Kathmandu', 'Nepal (UTC+05:45)'],
        ['Asia/Colombo', 'Sri Lanka (UTC+05:30)'],
        ['Asia/Dhaka', 'Bangladesh (UTC+06:00)'],
        ['Asia/Karachi', 'Pakistan (UTC+05:00)'],
        ['Asia/Dubai', 'UAE (UTC+04:00)'],
        ['Asia/Riyadh', 'Saudi Arabia (UTC+03:00)'],
        ['Asia/Singapore', 'Singapore (UTC+08:00)'],
        ['Africa/Nairobi', 'Kenya (UTC+03:00)'],
        ['Europe/London', 'United Kingdom (UTC+00:00 / +01:00)'],
        ['America/New_York', 'USA East (UTC−05:00 / −04:00)'],
        ['America/Los_Angeles', 'USA West (UTC−08:00 / −07:00)'],
        ['Australia/Sydney', 'Australia, Sydney (UTC+10:00 / +11:00)'],
    ];
    // What each quick setup turns on (same as OutletSettings.WithPreset on the server), to show which one is in use.
    const PRESET_VALUES = {
        'dine-in': { dineInEnabled: true, takeawayEnabled: true, defaultOrderType: 'DineIn', takeawayNeedsName: true, kitchenScreenEnabled: true, simpleCounter: false },
        cafe: { dineInEnabled: true, takeawayEnabled: true, defaultOrderType: 'Takeaway', takeawayNeedsName: true, kitchenScreenEnabled: true, simpleCounter: false },
        'takeaway-counter': { dineInEnabled: false, takeawayEnabled: true, defaultOrderType: 'Takeaway', takeawayNeedsName: false, kitchenScreenEnabled: true, simpleCounter: false },
        bakery: { dineInEnabled: true, takeawayEnabled: true, defaultOrderType: 'Takeaway', takeawayNeedsName: false, kitchenScreenEnabled: true, simpleCounter: true },
    };
    const currentPreset = (form) => (form
        ? PRESETS.find((p) => Object.entries(PRESET_VALUES[p]).every(([k, v]) => form[k] === v)) ?? null
        : null);

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

    // A quick setup changes several settings at once, so ask first.
    const [askPreset, setAskPreset] = useState(null);
    const applyPreset = (preset) => setAskPreset(preset);

    const doApplyPreset = async (preset) => {
        setAskPreset(null);
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
                        <p className="mb-0 fw-semibold" role="status">
                            {currentPreset(form)
                                ? t('settings.presetCurrent', { name: t(`settings.preset.${currentPreset(form)}`) })
                                : t('settings.presetCustom')}
                        </p>
                        <div className="d-flex flex-wrap gap-2">
                            {PRESETS.map(preset => (
                                <button
                                    key={preset}
                                    type="button"
                                    className={`btn ${currentPreset(form) === preset ? 'btn-primary' : 'btn-outline-secondary'} px-3`}
                                    aria-pressed={currentPreset(form) === preset}
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
                        {checkbox('simpleCounter', 'set-simple', 'settings.simpleCounter')}
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
                            id="set-text-size"
                            label={t('settings.menuTextSize')}
                            hint={t('settings.menuTextSizeHint')}
                            error={errors.menutextsize}
                        >
                            <select
                                id="set-text-size"
                                className="form-select"
                                value={form.menuTextSize ?? 'normal'}
                                onChange={text('menuTextSize')}
                            >
                                <option value="small">{t('settings.textSmall')}</option>
                                <option value="normal">{t('settings.textNormal')}</option>
                                <option value="large">{t('settings.textLarge')}</option>
                            </select>
                        </Field>
                        <Field
                            id="set-zone"
                            label={t('settings.timeZone')}
                            error={errors.timezone}
                        >
                            <select
                                id="set-zone"
                                className="form-select"
                                value={form.timeZone}
                                onChange={text('timeZone')}
                            >
                                {!TIME_ZONES.some(([id]) => id === form.timeZone) && <option value={form.timeZone}>{form.timeZone}</option>}
                                {TIME_ZONES.map(([id, label]) => <option key={id} value={id}>{label} – {id}</option>)}
                            </select>
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
            <ConfirmDialog open={askPreset !== null} title={t('confirm.presetTitle', { name: askPreset ? t(`settings.preset.${askPreset}`) : '' })}
                message={t('confirm.presetText')} confirmLabel={t('confirm.presetYes')}
                onCancel={() => setAskPreset(null)} onConfirm={() => doApplyPreset(askPreset)} />
        </main>
    );
}
