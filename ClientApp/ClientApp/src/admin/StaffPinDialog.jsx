import React, { useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import FormDialog from '../ui/FormDialog';
import { send, readErrors, Field } from '../ui/forms';

export default function StaffPinDialog({ staff, onClose, onSaved }) {
  const t = useT();
  const { showToast } = useToast();
  const [pin, setPin] = useState('');
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  async function save(value) {
    setBusy(true);
    const res = await send(`/api/staff/${staff.id}/pin`, 'PUT', { pin: value });
    setBusy(false);
    if (res.ok) {
      showToast(t('staff.pinSaved'));
      onSaved?.();
      onClose?.();
    } else {
      setErrors(await readErrors(res));
    }
  }

  return (
    <FormDialog
      open={!!staff}
      title={t('staff.pinTitle', { name: staff?.displayName ?? '' })}
      submitLabel={t('staff.save')}
      busy={busy}
      error={errors.general}
      onCancel={onClose}
      onSubmit={() => save(pin.trim())}
    >
      <Field id="staff-pin" label={t('staff.pin')} hint={t('staff.pinHint')} error={errors.pin}>
        <input
          id="staff-pin"
          type="text"
          style={{ WebkitTextSecurity: 'disc' }}
          data-lpignore="true"
          data-1p-ignore="true"
          inputMode="numeric"
          autoComplete="off"
          maxLength={6}
          className="form-control"
          value={pin}
          onChange={(e) => setPin(e.target.value)}
        />
      </Field>
      {staff?.hasPin && (
        <button
          type="button"
          className="btn btn-outline-danger btn-sm align-self-start"
          disabled={busy}
          onClick={() => save('')}
        >
          {t('staff.pinClear')}
        </button>
      )}
    </FormDialog>
  );
}
