import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { readErrors, send, Field } from '../ui/forms';
import '../components/OrderHistory.css';
import './StaffPage.css';

export default function StorePage() {
    const t = useT();
    const { showToast } = useToast();

    const FIELDS = [
        ['restaurantName', 'store.restaurantName'],
        ['outletName', 'store.outletName'],
        ['address', 'store.address'],
        ['phone', 'store.phone'],
        ['gstin', 'store.gstin'],
        ['fssai', 'store.fssai'],
        ['receiptFooter', 'store.receiptFooter']
    ];

    const [form, setForm] = useState({
        restaurantName: '',
        outletName: '',
        address: '',
        phone: '',
        gstin: '',
        fssai: '',
        receiptFooter: ''
    });
    const [hasLogo, setHasLogo] = useState(false);
    const [logoVersion, setLogoVersion] = useState(0);
    const [loadState, setLoadState] = useState('loading');
    const [errors, setErrors] = useState({});
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        try {
            const res = await fetch('/api/admin/store');
            if (res.ok) {
                const data = await res.json();
                setForm({
                    restaurantName: data.restaurantName ?? '',
                    outletName: data.outletName ?? '',
                    address: data.address ?? '',
                    phone: data.phone ?? '',
                    gstin: data.gstin ?? '',
                    fssai: data.fssai ?? '',
                    receiptFooter: data.receiptFooter ?? ''
                });
                setHasLogo(!!data.hasLogo);
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

    const save = async (e) => {
        e.preventDefault();
        const local = {};
        if (!form.restaurantName.trim()) local.restaurantname = t('store.nameRequired');
        if (!form.outletName.trim()) local.outletname = t('store.nameRequired');
        if (Object.keys(local).length > 0) {
            setErrors(local);
            return;
        }

        setBusy(true);
        try {
            const res = await fetch('/api/admin/store', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(form)
            });
            if (!res.ok) {
                setErrors(await readErrors(res));
            } else {
                setErrors({});
                showToast(t('store.saved'));
            }
        } catch (error) {
            setErrors({ general: t('common.error') });
        } finally {
            setBusy(false);
        }
    };

    const uploadLogo = async (e) => {
        const input = e.target;
        const file = input.files?.[0];
        if (!file) return;

        const data = new FormData();
        data.append('file', file);

        try {
            const res = await fetch('/api/admin/store/logo', {
                method: 'POST',
                body: data
            });
            if (!res.ok) {
                setErrors(await readErrors(res));
                return;
            }
            setErrors({});
            setHasLogo(true);
            setLogoVersion((v) => v + 1);
            showToast(t('store.logoSaved'));
        } catch (error) {
            setErrors({ logo: t('common.error') });
        } finally {
            input.value = '';
        }
    };

    const removeLogo = async () => {
        try {
            const res = await fetch('/api/admin/store/logo', {
                method: 'DELETE'
            });
            if (res.ok) {
                setHasLogo(false);
                showToast(t('store.logoRemoved'));
            }
        } catch (error) {
            setErrors({ logo: t('common.error') });
        }
    };

    return (
        <main className="pos-page">
            <div className="pos-page-head">
                <h1>{t('store.title')}</h1>
            </div>
            {loadState === 'loading' && <StateMessage kind="loading" />}
            {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
            {loadState === 'ready' && (
                <>
                    <form className="pos-card p-3 d-flex flex-column gap-3" noValidate onSubmit={save}>
                        {FIELDS.map(([key, label]) => (
                            <Field
                                key={key}
                                id={`store-${key}`}
                                label={t(label)}
                                error={errors[key.toLowerCase()]}
                            >
                                <input
                                    id={`store-${key}`}
                                    className="form-control"
                                    value={form[key]}
                                    onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
                                />
                                {key === 'gstin' && <p className="pos-hint">{t('store.gstinHint')}</p>}
                            </Field>
                        ))}
                        {errors.general && <p className="text-danger mb-0" role="alert">{errors.general || t('common.error')}</p>}
                        <button type="submit" className="btn btn-primary px-4 align-self-start" disabled={busy}>{t('store.save')}</button>
                    </form>
                    <section className="pos-card p-3 d-flex flex-column gap-2">
                        <h2 className="h5 mb-0">{t('store.logo')}</h2>
                        {hasLogo && (
                            <>
                                <img
                                    src={`/api/store/logo?v=${logoVersion}`}
                                    alt={t('store.logo')}
                                    style={{ maxHeight: 96, maxWidth: 240, objectFit: 'contain' }}
                                />
                                <button type="button" className="btn btn-outline-danger btn-sm px-3 align-self-start" onClick={removeLogo}>{t('store.removeLogo')}</button>
                            </>
                        )}
                        <label htmlFor="store-logo" className="form-label mb-0">{t('store.uploadLogo')}</label>
                        <input id="store-logo" type="file" accept="image/png,image/jpeg,image/webp" className="form-control" onChange={uploadLogo} />
                        <p className="pos-hint mb-0">{t('store.logoHint')}</p>
                        {errors.logo && <div className="invalid-feedback d-block">{errors.logo}</div>}
                    </section>
                </>
            )}
        </main>
    );
}
