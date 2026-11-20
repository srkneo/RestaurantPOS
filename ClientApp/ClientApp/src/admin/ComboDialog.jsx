import React, { useEffect, useRef, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import FormDialog from '../ui/FormDialog';
import Icon from '../ui/Icon';
import { readErrors, send } from '../ui/forms';

// Owner and manager: the items a combo includes. The bill shows one line at the combo's own price;
// the kitchen screen and the KOT list each part.
export default function ComboDialog({ item, items, onClose }) {
  const t = useT();
  const { showToast } = useToast();
  const [rows, setRows] = useState([]);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const tRef = useRef(t);
  tRef.current = t;

  useEffect(() => {
    if (!item) return undefined;
    let alive = true;
    setError(null);
    setRows([]);
    (async () => {
      try {
        const res = await fetch(`/api/admin/menu/items/${item.id}/combo`);
        if (!res.ok) throw new Error('load');
        const data = await res.json();
        if (alive) setRows(data.map((p) => ({ partItemId: String(p.partItemId), quantity: String(p.quantity) })));
      } catch {
        if (alive) setError(tRef.current('common.error'));
      }
    })();
    return () => {
      alive = false;
    };
  }, [item]);

  if (!item) return null;
  const choices = (items ?? []).filter((i) => i.id !== item.id);
  const update = (index, change) => setRows(rows.map((r, n) => (n === index ? { ...r, ...change } : r)));

  const save = async () => {
    if (rows.some((r) => !r.partItemId)) { setError(t('combo.needItem')); return; }
    if (rows.some((r) => !/^\d+$/.test(r.quantity) || Number(r.quantity) < 1 || Number(r.quantity) > 20)) { setError(t('combo.badQuantity')); return; }
    setBusy(true);
    setError(null);
    try {
      const res = await send(`/api/admin/menu/items/${item.id}/combo`, 'PUT',
        { parts: rows.map((r) => ({ partItemId: Number(r.partItemId), quantity: Number(r.quantity) })) });
      if (res.ok) {
        showToast(t('combo.saved', { name: item.name }));
        onClose(true);
      } else {
        const errors = await readErrors(res);
        setError(errors.parts || errors.general || t('common.error'));
      }
    } catch {
      setError(t('common.error'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <FormDialog open size="wide" title={t('combo.title', { name: item.name })} submitLabel={t('staff.save')} busy={busy} error={error}
      onCancel={() => onClose(false)} onSubmit={save}>
      <p className="pos-hint mb-0">{t('combo.hint')}</p>
      {rows.map((r, i) => (
        <div key={i} className="d-flex flex-wrap align-items-center gap-2">
          <select className="form-select w-auto flex-grow-1" aria-label={t('combo.part', { n: i + 1 })} value={r.partItemId}
            onChange={(e) => update(i, { partItemId: e.target.value })}>
            <option value="">{t('combo.choose')}</option>
            {choices.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
          <input className="form-control" style={{ width: '5rem' }} inputMode="numeric" aria-label={t('combo.quantity', { n: i + 1 })}
            value={r.quantity} onChange={(e) => update(i, { quantity: e.target.value })} />
          <button type="button" className="btn btn-outline-danger btn-sm" aria-label={t('combo.remove', { n: i + 1 })}
            onClick={() => setRows(rows.filter((_, n) => n !== i))}><Icon name="trash-can" /></button>
        </div>
      ))}
      <div>
        <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setRows([...rows, { partItemId: '', quantity: '1' }])}>
          {t('combo.add')}
        </button>
      </div>
    </FormDialog>
  );
}
