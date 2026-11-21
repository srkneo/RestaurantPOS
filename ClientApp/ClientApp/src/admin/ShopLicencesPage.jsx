import React, { useCallback, useEffect, useState } from 'react';
import LicenceMenuLink from './LicenceMenuLink';
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

const EMPTY_CREATE = { customer: '', plan: 'trial', days: '' };

// The POS owner's screen: the licences sold to shops. Create, extend, stop, restore, and free a key for a new PC.
export default function ShopLicencesPage() {
  const t = useT();
  const { showToast } = useToast();

  const [licences, setLicences] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState(EMPTY_CREATE);
  const [extending, setExtending] = useState(null);
  const [extendForm, setExtendForm] = useState({ days: '365', plan: 'yearly' });
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const [confirmStop, setConfirmStop] = useState(null);
  const [confirmReset, setConfirmReset] = useState(null);
  const [updating, setUpdating] = useState(null);
  const [resetCode, setResetCode] = useState(null);
  const [updateForm, setUpdateForm] = useState({ mode: 'auto', version: '' });
  const [search, setSearch] = useState('');

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/platform/licences');
      if (res.ok) {
        setLicences(await res.json());
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

  const dateText = (value, emptyKey) => {
    const date = parseServerDate(value);
    return date ? date.toLocaleDateString() : t(emptyKey);
  };

  const validDays = (text) => {
    const days = Number(text);
    return Number.isInteger(days) && days >= 1 && days <= 1100;
  };

  const submitCreate = async () => {
    const missing = {};
    if (form.customer.trim() === '') missing.customer = t('licences.customerRequired');
    if (form.days.trim() !== '' && !validDays(form.days)) missing.days = t('licences.daysInvalid');
    if (Object.keys(missing).length > 0) {
      setErrors(missing);
      return;
    }
    const body = { customer: form.customer.trim(), plan: form.plan, days: form.days.trim() === '' ? null : Number(form.days) };
    setBusy(true);
    try {
      const res = await send('/api/platform/licences', 'POST', body);
      if (!res.ok) {
        setErrors(await readErrors(res));
      } else {
        showToast(t('licences.created'));
        setCreating(false);
        setForm(EMPTY_CREATE);
        load();
      }
    } finally {
      setBusy(false);
    }
  };

  const submitExtend = async () => {
    if (!validDays(extendForm.days)) {
      setErrors({ days: t('licences.daysInvalid') });
      return;
    }
    setBusy(true);
    try {
      const res = await send(`/api/platform/licences/${extending.id}/extend`, 'POST', { days: Number(extendForm.days), plan: extendForm.plan });
      if (!res.ok) {
        setErrors(await readErrors(res));
      } else {
        showToast(t('licences.extended'));
        setExtending(null);
        load();
      }
    } finally {
      setBusy(false);
    }
  };

  const updatesText = (licence) => {
    if (licence.updateMode === 'paused') return t('licences.updates.paused');
    if (licence.updateMode === 'early') return t('licences.updates.early');
    if (licence.updateMode === 'pinned') return t('licences.updates.pinned', { version: licence.pinnedVersion || '' });
    return t('licences.updates.auto');
  };

  // A one-time code for an owner who forgot the password on the shop PC; support reads it out on the phone.
  const makeOwnerReset = async (licence) => {
    try {
      const res = await send(`/api/platform/licences/${licence.id}/owner-reset`, 'POST');
      if (res.ok) setResetCode({ ...(await res.json()), customer: licence.customer });
      else showToast(t('common.error'), 'error');
    } catch {
      showToast(t('common.error'), 'error');
    }
  };

  const openUpdates = (licence) => {
    setErrors({});
    setUpdateForm({ mode: licence.updateMode || 'auto', version: licence.pinnedVersion || '' });
    setUpdating(licence);
  };

  const submitUpdates = async () => {
    const version = updateForm.version.trim().replace(/^v/i, '');
    if (updateForm.mode === 'pinned' && !/^\d+\.\d+\.\d+$/.test(version)) {
      setErrors({ version: t('licences.updatesVersionInvalid') });
      return;
    }
    setBusy(true);
    try {
      const res = await send(`/api/platform/licences/${updating.id}/updates`, 'POST',
        { mode: updateForm.mode, version: updateForm.mode === 'pinned' ? version : null });
      if (!res.ok) {
        setErrors(await readErrors(res));
      } else {
        showToast(t('licences.updatesSaved', { name: updating.customer }));
        setUpdating(null);
        load();
      }
    } finally {
      setBusy(false);
    }
  };

  const act = async (licence, action, toastKey) => {
    const res = await send(`/api/platform/licences/${licence.id}/${action}`, 'POST');
    setConfirmStop(null);
    setConfirmReset(null);
    if (res.ok) {
      showToast(t(toastKey, { name: licence.customer }));
      load();
    } else {
      showToast(t('common.error'));
    }
  };

  const copyKey = async (licence) => {
    try {
      await navigator.clipboard.writeText(licence.key);
      showToast(t('licences.copied'));
    } catch {
      showToast(t('common.error'));
    }
  };

  const openExtend = (licence) => {
    setErrors({});
    setExtendForm({ days: '365', plan: licence.plan === 'trial' ? 'yearly' : licence.plan });
    setExtending(licence);
  };

  const setField = (name) => (e) => setForm({ ...form, [name]: e.target.value });
  const setExtendField = (name) => (e) => setExtendForm({ ...extendForm, [name]: e.target.value });
  const active = (licence) => licence.state === 'Trial' || licence.state === 'Active';

  // Search by customer name or licence key.
  const query = search.trim().toLowerCase();
  const shown = query
    ? licences.filter((l) => `${l.customer ?? ''} ${l.key ?? ''}`.toLowerCase().includes(query))
    : licences;
  const { page, pageCount, pageItems, setPage, total } = usePaged(shown, 50);

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('licences.title')}</h1>
        <button type="button" className="btn btn-primary px-3" onClick={() => { setCreating(true); setErrors({}); setForm(EMPTY_CREATE); }}>
          {t('licences.add')}
        </button>
      </div>
      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && licences.length === 0 && <StateMessage kind="empty" message={t('licences.empty')} />}
      {loadState === 'ready' && licences.length > 0 && (
        <div className="mb-2" style={{ maxWidth: 360 }}>
          <label htmlFor="licences-search" className="form-label">{t('licences.search')}</label>
          <input id="licences-search" type="search" className="form-control" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
      )}
      {loadState === 'ready' && licences.length > 0 && shown.length === 0 && <StateMessage kind="empty" message={t('licences.noMatch')} />}
      {loadState === 'ready' && shown.length > 0 && (
        <div className="pos-card table-responsive">
          <table className="table align-middle">
            <thead>
              <tr>
                <th>{t('licences.customer')}</th>
                <th>{t('licences.key')}</th>
                <th>{t('licence.plan')}</th>
                <th>{t('licences.state')}</th>
                <th>{t('licences.paidUntil')}</th>
                <th>{t('licences.lastSeen')}</th>
                <th>{t('licences.version')}</th>
                <th>{t('licences.updates')}</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {pageItems.map((licence) => (
                <tr key={licence.id}>
                  <td className="fw-semibold">{licence.customer}</td>
                  <td>
                    <code>{licence.key}</code>{' '}
                    <button type="button" className="btn btn-link btn-sm p-0" onClick={() => copyKey(licence)}>{t('licences.copy')}</button>
                  </td>
                  <td>{t(`licence.plan.${licence.plan}`)}</td>
                  <td>
                    <span className={`pos-status ${active(licence) ? 'pos-status-active' : 'pos-status-inactive'}`}>
                      {t(`licences.state.${licence.state}`)}
                    </span>
                  </td>
                  <td>{dateText(licence.paidUntil, 'licences.never')}</td>
                  <td>{dateText(licence.lastSeenAt, 'licences.never')}</td>
                  <td>{licence.lastVersion || '–'}</td>
                  <td>{updatesText(licence)}</td>
                  <td className="d-flex flex-wrap gap-2">
                    <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => openExtend(licence)}>{t('licences.extend')}</button>
                    <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => openUpdates(licence)}>{t('licences.updates')}</button>
                    <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => makeOwnerReset(licence)}>{t('licences.ownerReset')}</button>
                    <LicenceMenuLink licence={licence} onSaved={load} />
                    {licence.revoked
                      ? <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => act(licence, 'restore', 'licences.restored')}>{t('licences.restore')}</button>
                      : <button type="button" className="btn btn-outline-danger btn-sm px-3" onClick={() => setConfirmStop(licence)}>{t('licences.stop')}</button>}
                    {licence.deviceBound && (
                      <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => setConfirmReset(licence)}>{t('licences.resetPc')}</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {loadState === 'ready' && <Pager page={page} pageCount={pageCount} total={total} onPage={setPage} />}
      <FormDialog open={creating} title={t('licences.createTitle')} submitLabel={t('licences.create')} busy={busy}
        error={errors.general !== undefined ? (errors.general || t('common.error')) : null}
        onCancel={() => setCreating(false)} onSubmit={submitCreate}>
        <Field id="licences-customer" label={t('licences.customerName')} error={errors.customer}>
          <input className="form-control" id="licences-customer" value={form.customer} onChange={setField('customer')} maxLength={200} />
        </Field>
        <Field id="licences-plan" label={t('licence.plan')} error={errors.plan}>
          <select className="form-select" id="licences-plan" value={form.plan} onChange={setField('plan')}>
            <option value="trial">{t('licence.plan.trial')}</option>
            <option value="yearly">{t('licence.plan.yearly')}</option>
          </select>
        </Field>
        <Field id="licences-days" label={t('licences.days')} hint={t('licences.daysHint')} error={errors.days}>
          <input className="form-control" id="licences-days" value={form.days} onChange={setField('days')} inputMode="numeric" />
        </Field>
      </FormDialog>
      <FormDialog open={extending !== null} title={t('licences.extendTitle', { name: extending?.customer ?? '' })} submitLabel={t('licences.extendSave')} busy={busy}
        error={errors.general !== undefined ? (errors.general || t('common.error')) : null}
        onCancel={() => setExtending(null)} onSubmit={submitExtend}>
        <Field id="licences-extend-days" label={t('licences.extendDays')} error={errors.days}>
          <input className="form-control" id="licences-extend-days" value={extendForm.days} onChange={setExtendField('days')} inputMode="numeric" />
        </Field>
        <Field id="licences-extend-plan" label={t('licence.plan')} error={errors.plan}>
          <select className="form-select" id="licences-extend-plan" value={extendForm.plan} onChange={setExtendField('plan')}>
            <option value="trial">{t('licence.plan.trial')}</option>
            <option value="yearly">{t('licence.plan.yearly')}</option>
          </select>
        </Field>
      </FormDialog>
      <ConfirmDialog open={resetCode !== null} title={t('licences.ownerResetTitle', { name: resetCode?.customer ?? '' })}
        message={t('licences.ownerResetText', { code: resetCode?.code ?? '', time: parseServerDate(resetCode?.expiresAt)?.toLocaleString() ?? '' })}
        confirmLabel={t('licences.ownerResetDone')} onConfirm={() => setResetCode(null)} onCancel={() => setResetCode(null)} />
      <FormDialog open={updating !== null} title={t('licences.updatesTitle', { name: updating?.customer ?? '' })} submitLabel={t('licences.updatesSave')} busy={busy}
        error={errors.general !== undefined ? (errors.general || t('common.error')) : null}
        onCancel={() => setUpdating(null)} onSubmit={submitUpdates}>
        <Field id="licences-updates-mode" label={t('licences.updatesMode')} hint={t('licences.updatesModeHint')} error={errors.mode}>
          <select className="form-select" id="licences-updates-mode" value={updateForm.mode}
            onChange={(e) => setUpdateForm({ ...updateForm, mode: e.target.value })}>
            <option value="auto">{t('licences.updates.auto')}</option>
            <option value="early">{t('licences.updates.early')}</option>
            <option value="paused">{t('licences.updates.paused')}</option>
            <option value="pinned">{t('licences.updates.pinned', { version: '…' })}</option>
          </select>
        </Field>
        {updateForm.mode === 'pinned' && (
          <Field id="licences-updates-version" label={t('licences.updatesVersion')} error={errors.version}>
            <input className="form-control" id="licences-updates-version" value={updateForm.version} placeholder="1.7.3" maxLength={20}
              onChange={(e) => setUpdateForm({ ...updateForm, version: e.target.value })} />
          </Field>
        )}
      </FormDialog>
      <ConfirmDialog open={confirmStop !== null} title={t('licences.stopTitle', { name: confirmStop?.customer ?? '' })} message={t('licences.stopHint')}
        confirmLabel={t('licences.stopConfirm')} danger onConfirm={() => act(confirmStop, 'revoke', 'licences.stopped')} onCancel={() => setConfirmStop(null)} />
      <ConfirmDialog open={confirmReset !== null} title={t('licences.resetTitle', { name: confirmReset?.customer ?? '' })} message={t('licences.resetHint')}
        confirmLabel={t('licences.resetConfirm')} onConfirm={() => act(confirmReset, 'reset-device', 'licences.resetDone')} onCancel={() => setConfirmReset(null)} />
    </main>
  );
}
