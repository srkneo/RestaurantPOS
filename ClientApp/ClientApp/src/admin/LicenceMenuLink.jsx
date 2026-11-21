import React, { useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import FormDialog from '../ui/FormDialog';
import { Field } from '../ui/forms';

// v1.7 menu sync: the platform owner picks the cloud restaurant whose menu a shop PC copies (or none).
// The restaurant list is loaded only when the dialog opens.
export default function LicenceMenuLink({ licence, onSaved }) {
  const t = useT();
  const { showToast } = useToast();
  const [open, setOpen] = useState(false);
  const [tenants, setTenants] = useState([]);
  const [value, setValue] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const start = async () => {
    setValue(licence.tenantId ? String(licence.tenantId) : '');
    setError(null);
    setOpen(true);
    try {
      const res = await fetch('/api/platform/tenants');
      if (res.ok) {
        const list = await res.json();
        setTenants(Array.isArray(list) ? list : []);
      }
    } catch {
      setTenants([]);
    }
  };

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(`/api/platform/licences/${licence.id}/restaurant`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tenantId: value ? Number(value) : null }),
      });
      if (res.ok) {
        showToast(t('licences.menuSaved'));
        setOpen(false);
        onSaved?.();
      } else {
        const data = await res.json().catch(() => ({}));
        setError(data?.message || t('licences.menuFailed'));
      }
    } catch {
      setError(t('licences.menuFailed'));
    } finally {
      setBusy(false);
    }
  };

  const fieldId = `licence-menu-${licence.id}`;
  return (
    <>
      <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={start}>
        {licence.tenantId ? t('licences.menuLinked') : t('licences.menuSource')}
      </button>
      <FormDialog open={open} title={t('licences.menuTitle', { name: licence.customer })} submitLabel={t('licences.menuSave')} busy={busy}
        error={error} onCancel={() => setOpen(false)} onSubmit={save}>
        <Field id={fieldId} label={t('licences.menuLabel')} hint={t('licences.menuHint')}>
          <select className="form-select" id={fieldId} value={value} onChange={(e) => setValue(e.target.value)}>
            <option value="">{t('licences.menuOwn')}</option>
            {tenants.map((tenant) => <option key={tenant.id} value={String(tenant.id)}>{tenant.name}</option>)}
          </select>
        </Field>
      </FormDialog>
    </>
  );
}
