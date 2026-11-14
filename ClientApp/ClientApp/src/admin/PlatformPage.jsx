import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import FormDialog from '../ui/FormDialog';
import ConfirmDialog from '../ui/ConfirmDialog';
import { readErrors, send, Field } from '../ui/forms';
import { parseServerDate } from '../ui/format';
import '../components/OrderHistory.css';
import './StaffPage.css';

const EMPTY_FORM = { restaurantName: '', outletName: '', ownerName: '', ownerEmail: '', ownerPassword: '' };

export default function PlatformPage() {
    const t = useT();
    const { showToast } = useToast();

    const [tenants, setTenants] = useState([]);
    const [loadState, setLoadState] = useState('loading');
    const [adding, setAdding] = useState(false);
    const [form, setForm] = useState(EMPTY_FORM);
    const [errors, setErrors] = useState({});
    const [busy, setBusy] = useState(false);
    const [confirm, setConfirm] = useState(null);

    const load = useCallback(async () => {
        try {
            const res = await fetch('/api/platform/tenants');
            if (res.ok) {
                setTenants(await res.json());
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

    const lastOrder = (value) => {
        const date = parseServerDate(value);
        return date ? date.toLocaleString() : t('platform.never');
    };

    const setField = (name) => (e) => setForm({ ...form, [name]: e.target.value });

    const submit = async () => {
        const required = ['restaurantName', 'outletName', 'ownerName', 'ownerEmail', 'ownerPassword'];
        const missing = {};
        required.forEach((name) => {
            if (form[name].trim() === '') {
                missing[name.toLowerCase()] = t('platform.required');
            }
        });
        if (Object.keys(missing).length > 0) {
            setErrors(missing);
            return;
        }
        const body = {
            restaurantName: form.restaurantName.trim(),
            outletName: form.outletName.trim(),
            ownerName: form.ownerName.trim(),
            ownerEmail: form.ownerEmail.trim(),
            ownerPassword: form.ownerPassword
        };
        setBusy(true);
        try {
            const res = await send('/api/platform/tenants', 'POST', body);
            if (!res.ok) {
                setErrors(await readErrors(res));
            } else {
                showToast(t('platform.added'));
                setAdding(false);
                setForm(EMPTY_FORM);
                load();
            }
        } finally {
            setBusy(false);
        }
    };

    const changeStatus = async (tenant, active) => {
        const res = await send(`/api/platform/tenants/${tenant.id}/${active ? 'enable' : 'disable'}`, 'POST');
        setConfirm(null);
        if (res.ok) {
            showToast(t(active ? 'platform.enabledToast' : 'platform.disabledToast', { name: tenant.name }));
            load();
        } else {
            showToast(t('common.error'));
        }
    };

    return (
        <main className="pos-page">
            <div className="pos-page-head">
                <h1>{t('platform.title')}</h1>
                <button type="button" className="btn btn-primary px-3" onClick={() => { setAdding(true); setErrors({}); setForm(EMPTY_FORM); }}>
                    {t('platform.add')}
                </button>
            </div>
            {loadState === 'loading' && <StateMessage kind="loading" />}
            {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
            {loadState === 'ready' && tenants.length === 0 && <StateMessage kind="empty" message={t('platform.empty')} />}
            {loadState === 'ready' && tenants.length > 0 && (
                <div className="pos-card table-responsive">
                    <table className="table align-middle">
                        <thead>
                            <tr>
                                <th>{t('platform.restaurant')}</th>
                                <th>{t('platform.outlets')}</th>
                                <th>{t('platform.staff')}</th>
                                <th>{t('platform.lastOrder')}</th>
                                <th>{t('staff.status')}</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {tenants.map((tenant) => (
                                <tr key={tenant.id}>
                                    <td className="fw-semibold">{tenant.name}</td>
                                    <td>{tenant.outletCount}</td>
                                    <td>{tenant.staffCount}</td>
                                    <td>{lastOrder(tenant.lastOrderAt)}</td>
                                    <td>
                                        <span className={`pos-status ${tenant.isActive ? 'pos-status-active' : 'pos-status-inactive'}`}>
                                            {tenant.isActive ? t('platform.active') : t('platform.disabled')}
                                        </span>
                                    </td>
                                    <td>
                                        {tenant.isActive
                                            ? <button type="button" className="btn btn-outline-danger btn-sm px-3" onClick={() => setConfirm(tenant)}>{t('platform.disable')}</button>
                                            : <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => changeStatus(tenant, true)}>{t('platform.enable')}</button>}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            <FormDialog open={adding} title={t('platform.add')} submitLabel={t('platform.create')} busy={busy}
                error={errors.general !== undefined ? (errors.general || t('common.error')) : null}
                onCancel={() => setAdding(false)} onSubmit={submit}>
                <Field id="platform-restaurant" label={t('platform.restaurantName')} error={errors.restaurantname}>
                    <input className="form-control" id="platform-restaurant" value={form.restaurantName} onChange={setField('restaurantName')} maxLength={200} />
                </Field>
                <Field id="platform-outlet" label={t('platform.outletName')} hint={t('platform.outletHint')} error={errors.outletname}>
                    <input className="form-control" id="platform-outlet" value={form.outletName} onChange={setField('outletName')} maxLength={200} />
                </Field>
                <Field id="platform-owner" label={t('platform.ownerName')} error={errors.ownername}>
                    <input className="form-control" id="platform-owner" value={form.ownerName} onChange={setField('ownerName')} maxLength={100} />
                </Field>
                <Field id="platform-email" label={t('platform.ownerEmail')} error={errors.owneremail}>
                    <input className="form-control" id="platform-email" value={form.ownerEmail} onChange={setField('ownerEmail')} type="email" autoComplete="off" />
                </Field>
                <Field id="platform-password" label={t('platform.ownerPassword')} hint={t('platform.passwordHint')} error={errors.ownerpassword}>
                    <input className="form-control" id="platform-password" value={form.ownerPassword} onChange={setField('ownerPassword')} type="password" autoComplete="new-password" />
                </Field>
            </FormDialog>
            <ConfirmDialog open={confirm !== null} title={t('platform.disableTitle', { name: confirm?.name ?? '' })} message={t('platform.disableHint')}
                confirmLabel={t('platform.disable')} danger onConfirm={() => changeStatus(confirm, false)} onCancel={() => setConfirm(null)} />
        </main>
    );
}
