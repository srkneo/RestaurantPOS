import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import FormDialog from '../ui/FormDialog';
import StateMessage from '../ui/StateMessage';
import '../components/OrderHistory.css';
import './StaffPage.css';

const ROLES = ['admin', 'manager', 'cashier', 'kitchen'];
const NEW_STAFF = { displayName: '', email: '', role: 'cashier', password: '', isDevice: false };

// Turns a ValidationProblem response into { field: message }.
async function readErrors(res) {
  try {
    const data = await res.json();
    if (data?.errors) {
      return Object.fromEntries(Object.entries(data.errors).map(([k, v]) => [k.toLowerCase(), Array.isArray(v) ? v.join(' ') : String(v)]));
    }
    return { general: data?.message || data?.title || '' };
  } catch {
    return { general: '' };
  }
}

function send(url, method, body) {
  return fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
}

function Field({ id, label, error, hint, children }) {
  return (
    <div>
      <label htmlFor={id} className="form-label">{label}</label>
      {children}
      {hint && !error && <p className="pos-hint">{hint}</p>}
      {error && <div className="invalid-feedback d-block">{error}</div>}
    </div>
  );
}

export default function StaffPage() {
  const t = useT();
  const { showToast } = useToast();
  const [staff, setStaff] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [mode, setMode] = useState(null); // 'add' | 'edit' | 'password'
  const [current, setCurrent] = useState(null);
  const [form, setForm] = useState(NEW_STAFF);
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/staff');
      if (!res.ok) throw new Error('load failed');
      setStaff(await res.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const set = (field) => (e) => {
    const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
    setForm((f) => ({ ...f, [field]: value, ...(field === 'isDevice' && value ? { role: 'kitchen' } : {}) }));
  };

  const open = (nextMode, member, values) => {
    setCurrent(member);
    setForm(values);
    setErrors({});
    setMode(nextMode);
  };
  const close = useCallback(() => setMode(null), []);

  const submit = async () => {
    const local = {};
    if (mode !== 'password' && !form.displayName?.trim()) local.displayname = t('staff.nameRequired');
    if (mode === 'add' && !form.email?.trim()) local.email = t('staff.emailRequired');
    if (mode !== 'edit' && !form.password) local.password = t('staff.passwordRequired');
    if (Object.keys(local).length > 0) {
      setErrors(local);
      return;
    }
    setBusy(true);
    try {
      let res;
      if (mode === 'add') {
        res = await send('/api/staff', 'POST', {
          displayName: form.displayName.trim(), email: form.email.trim(), role: form.role, password: form.password, isDevice: form.isDevice,
        });
      } else if (mode === 'edit') {
        res = await send(`/api/staff/${current.id}`, 'PUT', { displayName: form.displayName.trim(), role: form.role, isActive: form.isActive });
      } else {
        res = await send(`/api/staff/${current.id}/password`, 'POST', { password: form.password });
      }
      if (!res.ok) {
        setErrors(await readErrors(res));
        return;
      }
      showToast(t(mode === 'add' ? 'staff.added' : mode === 'edit' ? 'staff.saved' : 'staff.passwordChanged'));
      setMode(null);
      load();
    } catch {
      setErrors({ general: t('pos.networkError') });
    } finally {
      setBusy(false);
    }
  };

  const generalError = errors.general !== undefined ? (errors.general || t('common.error')) : null;
  const roleSelect = (disabled) => (
    <Field id="staff-role" label={t('staff.role')} error={errors.role}>
      <select id="staff-role" className={`form-select${errors.role ? ' is-invalid' : ''}`} value={form.role} onChange={set('role')} disabled={disabled}>
        {ROLES.map((r) => <option key={r} value={r}>{t(`role.${r}`)}</option>)}
      </select>
    </Field>
  );
  const nameField = (
    <Field id="staff-name" label={t('staff.name')} error={errors.displayname}>
      <input id="staff-name" className={`form-control${errors.displayname ? ' is-invalid' : ''}`} value={form.displayName} onChange={set('displayName')} maxLength={100} />
    </Field>
  );
  const passwordField = (label) => (
    <Field id="staff-password" label={label} error={errors.password} hint={t('staff.passwordHint')}>
      <input id="staff-password" type="password" autoComplete="new-password" className={`form-control${errors.password ? ' is-invalid' : ''}`}
        value={form.password} onChange={set('password')} />
    </Field>
  );

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('staff.title')}</h1>
        <button type="button" className="btn btn-primary px-3" onClick={() => open('add', null, NEW_STAFF)}>{t('staff.add')}</button>
      </div>

      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && staff.length === 0 && <StateMessage kind="empty" message={t('staff.empty')} />}
      {loadState === 'ready' && staff.length > 0 && (
        <div className="pos-card table-responsive">
          <table className="table align-middle">
            <thead>
              <tr>
                <th scope="col">{t('staff.name')}</th>
                <th scope="col">{t('staff.email')}</th>
                <th scope="col">{t('staff.role')}</th>
                <th scope="col">{t('staff.type')}</th>
                <th scope="col">{t('staff.status')}</th>
                <th scope="col" className="text-end">{t('staff.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {staff.map((s) => (
                <tr key={s.id}>
                  <td className="fw-semibold">{s.displayName}</td>
                  <td>{s.email}</td>
                  <td>{s.role ? t(`role.${s.role}`) : ''}</td>
                  <td>{s.isDevice ? t('staff.deviceShort') : t('staff.person')}</td>
                  <td>
                    <span className={`pos-status ${s.isActive ? 'pos-status-active' : 'pos-status-inactive'}`}>
                      {s.isActive ? t('staff.active') : t('staff.inactive')}
                    </span>
                  </td>
                  <td>
                    <div className="pos-staff-actions">
                      <button type="button" className="btn btn-outline-secondary btn-sm px-3"
                        onClick={() => open('edit', s, { displayName: s.displayName, role: s.role, isActive: s.isActive })}>
                        {t('staff.edit')}
                      </button>
                      <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => open('password', s, { password: '' })}>
                        {t('staff.resetPassword')}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <FormDialog open={mode === 'add'} title={t('staff.addTitle')} submitLabel={t('staff.save')} busy={busy} error={generalError}
        onCancel={close} onSubmit={submit}>
        {nameField}
        <Field id="staff-email" label={t('staff.email')} error={errors.email}>
          <input id="staff-email" type="email" autoComplete="off" className={`form-control${errors.email ? ' is-invalid' : ''}`}
            value={form.email} onChange={set('email')} />
        </Field>
        <div>
          <div className="pos-check">
            <input id="staff-device" type="checkbox" className="form-check-input" checked={!!form.isDevice} onChange={set('isDevice')} />
            <label htmlFor="staff-device" className="mb-0 fw-semibold">{t('staff.device')}</label>
          </div>
          <p className="pos-hint">{t('staff.deviceHint')}</p>
        </div>
        {roleSelect(!!form.isDevice)}
        {passwordField(t('staff.password'))}
      </FormDialog>

      <FormDialog open={mode === 'edit'} title={t('staff.editTitle', { name: current?.displayName ?? '' })} submitLabel={t('staff.save')}
        busy={busy} error={generalError} onCancel={close} onSubmit={submit}>
        {nameField}
        {roleSelect(!!current?.isDevice)}
        <div className="pos-check">
          <input id="staff-active" type="checkbox" className="form-check-input" checked={!!form.isActive} onChange={set('isActive')} />
          <label htmlFor="staff-active" className="mb-0 fw-semibold">{t('staff.active')}</label>
        </div>
      </FormDialog>

      <FormDialog open={mode === 'password'} title={t('staff.passwordTitle', { name: current?.displayName ?? '' })} submitLabel={t('staff.save')}
        busy={busy} error={generalError} onCancel={close} onSubmit={submit}>
        {passwordField(t('staff.newPassword'))}
      </FormDialog>
    </main>
  );
}
