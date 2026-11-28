import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import Pager, { usePaged } from '../ui/Pager';
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
    const [demoFor, setDemoFor] = useState(null);
    const [supportFor, setSupportFor] = useState(null);
    const [search, setSearch] = useState('');
    const [paying, setPaying] = useState(null);
    const [payForm, setPayForm] = useState({ plan: 'monthly', amount: '', reference: '' });
    const [prices, setPrices] = useState({ monthly: 1000, yearly: 10000 });

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

    // A sample menu and three orders of today in an empty restaurant, for demos.
    const fillDemo = async () => {
        const tenant = demoFor;
        setDemoFor(null);
        const res = await send(`/api/platform/tenants/${tenant.id}/demo`, 'POST', {});
        if (res.ok) {
            showToast(t('platform.demoDone', { name: tenant.name }));
            load();
        } else {
            const errors = await readErrors(res);
            showToast(errors.general || t('common.error'), 'error');
        }
    };

    const openSupport = async () => {
        const tenant = supportFor;
        setSupportFor(null);
        const res = await send(`/api/platform/tenants/${tenant.id}/support-login`, 'POST', {});
        if (res.ok) window.location.assign('/');
        else showToast((await readErrors(res)).support ?? t('common.error'), 'danger');
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

    // Subscription column: free use, trial / paid-up date, overdue, stopped.
    const subscriptionText = (tenant) => {
        if (!tenant.paidUntil) return t('platform.sub.none');
        const date = new Date(`${tenant.paidUntil}T00:00:00`).toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
        return t(`platform.sub.${tenant.state}`, { date });
    };

    // Prices come from the server when the dialog opens (Pricing__Monthly / Pricing__Yearly).
    const openPay = async (tenant) => {
        setPaying(tenant);
        setErrors({});
        let current = prices;
        try {
            const res = await fetch('/api/signup/plans');
            if (res.ok) {
                const data = await res.json();
                current = { monthly: data.monthly, yearly: data.yearly };
                setPrices(current);
            }
        } catch {
            // keeps the last prices
        }
        setPayForm({ plan: 'monthly', amount: String(current.monthly), reference: '' });
    };

    const changePlan = (plan) => setPayForm({ ...payForm, plan, amount: String(plan === 'yearly' ? prices.yearly : prices.monthly) });

    const savePay = async () => {
        setBusy(true);
        const res = await send(`/api/platform/tenants/${paying.id}/payments`, 'POST', {
            plan: payForm.plan, amount: Number(payForm.amount), reference: payForm.reference.trim() || null,
        });
        setBusy(false);
        if (!res.ok) {
            setErrors(await readErrors(res));
            return;
        }
        const data = await res.json();
        const date = new Date(`${data.paidUntil}T00:00:00`).toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
        showToast(t('platform.paymentSaved', { name: paying.name, date }));
        setPaying(null);
        load();
    };

    const query = search.trim().toLowerCase();
    const shownTenants = query ? tenants.filter((x) => (x.name ?? '').toLowerCase().includes(query)) : tenants;
    const { page, pageCount, pageItems, setPage, total } = usePaged(shownTenants, 50);

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
                <div className="mb-2" style={{ maxWidth: 360 }}>
                    <label htmlFor="platform-search" className="form-label">{t('platform.search')}</label>
                    <input id="platform-search" type="search" className="form-control" value={search} onChange={(e) => setSearch(e.target.value)} />
                </div>
            )}
            {loadState === 'ready' && tenants.length > 0 && shownTenants.length === 0 && <StateMessage kind="empty" message={t('platform.noMatch')} />}
            {loadState === 'ready' && shownTenants.length > 0 && (
                <div className="pos-card table-responsive">
                    <table className="table align-middle">
                        <thead>
                            <tr>
                                <th>{t('platform.restaurant')}</th>
                                <th>{t('platform.outlets')}</th>
                                <th>{t('platform.staff')}</th>
                                <th>{t('platform.lastOrder')}</th>
                                <th>{t('platform.subscription')}</th>
                                <th>{t('staff.status')}</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {pageItems.map((tenant) => (
                                <tr key={tenant.id}>
                                    <td className="fw-semibold">{tenant.name}</td>
                                    <td>{tenant.outletCount}</td>
                                    <td>{tenant.staffCount}</td>
                                    <td>{lastOrder(tenant.lastOrderAt)}</td>
                                    <td className={tenant.state === 'suspended' ? 'text-danger fw-semibold' : tenant.state === 'overdue' ? 'text-warning fw-semibold' : ''}>{subscriptionText(tenant)}</td>
                                    <td>
                                        <span className={`pos-status ${tenant.isActive ? 'pos-status-active' : 'pos-status-inactive'}`}>
                                            {tenant.isActive ? t('platform.active') : t('platform.disabled')}
                                        </span>
                                    </td>
                                    <td>
                                        {tenant.isActive
                                            ? <button type="button" className="btn btn-outline-danger btn-sm px-3" onClick={() => setConfirm(tenant)}>{t('platform.disable')}</button>
                                            : <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => changeStatus(tenant, true)}>{t('platform.enable')}</button>}
                                        <button type="button" className="btn btn-outline-primary btn-sm px-3 ms-2" onClick={() => openPay(tenant)}>{t('platform.payment')}</button>
                                        <a className="btn btn-outline-secondary btn-sm px-3 ms-2" href={`/platform/tenants/${tenant.id}/activity`}>{t('platform.activity')}</a>
                                        {!tenant.lastOrderAt && (
                                            <button type="button" className="btn btn-outline-primary btn-sm px-3 ms-2" onClick={() => setDemoFor(tenant)}>{t('platform.demo')}</button>
                                        )}
                                        {tenant.isActive && (
                                            <button type="button" className="btn btn-outline-danger btn-sm px-3 ms-2" onClick={() => setSupportFor(tenant)}>{t('platform.support')}</button>
                                        )}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            {loadState === 'ready' && <Pager page={page} pageCount={pageCount} total={total} onPage={setPage} />}
            <FormDialog open={!!paying} title={t('platform.paymentTitle', { name: paying?.name ?? '' })} submitLabel={t('platform.paymentSave')} busy={busy}
                error={errors.general !== undefined ? (errors.general || t('common.error')) : null}
                onCancel={() => setPaying(null)} onSubmit={savePay}>
                <Field id="pay-plan" label={t('platform.plan')} error={errors.plan}>
                    <select id="pay-plan" className="form-select" value={payForm.plan} onChange={(e) => changePlan(e.target.value)}>
                        <option value="monthly">{t('platform.planMonthly', { price: prices.monthly })}</option>
                        <option value="yearly">{t('platform.planYearly', { price: prices.yearly })}</option>
                    </select>
                </Field>
                <Field id="pay-amount" label={t('platform.amount')} error={errors.amount}>
                    <input id="pay-amount" type="number" min="0" step="1" className="form-control" value={payForm.amount}
                        onChange={(e) => setPayForm({ ...payForm, amount: e.target.value })} />
                </Field>
                <Field id="pay-reference" label={t('platform.reference')} error={errors.reference}>
                    <input id="pay-reference" className="form-control" maxLength={60} value={payForm.reference}
                        onChange={(e) => setPayForm({ ...payForm, reference: e.target.value })} />
                </Field>
            </FormDialog>
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
            <ConfirmDialog open={demoFor !== null} title={t('platform.demoTitle', { name: demoFor?.name ?? '' })} message={t('platform.demoHint')}
                confirmLabel={t('platform.demo')} onConfirm={fillDemo} onCancel={() => setDemoFor(null)} />
            <ConfirmDialog open={supportFor !== null} title={t('platform.supportTitle')} message={t('platform.supportHint')}
                confirmLabel={t('platform.supportOpen')} onConfirm={openSupport} onCancel={() => setSupportFor(null)} />
        </main>
    );
}
