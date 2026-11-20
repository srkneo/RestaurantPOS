import React, { useEffect, useRef, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import FormDialog from '../ui/FormDialog';
import Icon from '../ui/Icon';
import { readErrors, send } from '../ui/forms';

const blankRow = () => ({ id: null, name: '', price: '', isAvailable: true });
const toRows = (list) => (list ?? []).map((o) => ({ id: o.id, name: o.name, price: String(o.price), isAvailable: o.isAvailable }));
const toBody = (rows) => rows.map((r) => ({ id: r.id, name: r.name.trim(), price: Number(r.price), isAvailable: r.isAvailable }));
const validPrice = (text) => /^\d+(\.\d{1,2})?$/.test(String(text).trim());

function OptionRows({ kind, title, hint, rows, setRows }) {
  const t = useT();
  const update = (index, change) => setRows(rows.map((r, n) => (n === index ? { ...r, ...change } : r)));
  return (
    <div role="group" aria-label={title} className="d-flex flex-column gap-2">
      <div>
        <div className="fw-semibold">{title}</div>
        <div className="small text-body-secondary">{hint}</div>
      </div>
      {rows.map((r, i) => (
        <div key={i} className="d-flex flex-wrap align-items-center gap-2">
          <input className="form-control w-auto flex-grow-1" maxLength={40} value={r.name}
            aria-label={t('options.rowName', { kind: title, n: i + 1 })} placeholder={t(`options.${kind}Example`)}
            onChange={(e) => update(i, { name: e.target.value })} />
          <input className="form-control" style={{ width: '7rem' }} inputMode="decimal" value={r.price}
            aria-label={t('options.rowPrice', { kind: title, n: i + 1 })} placeholder="0"
            onChange={(e) => update(i, { price: e.target.value })} />
          <div className="form-check form-switch mb-0">
            <input className="form-check-input" type="checkbox" role="switch" id={`opt-${kind}-${i}`} checked={r.isAvailable}
              onChange={() => update(i, { isAvailable: !r.isAvailable })} />
            <label className="form-check-label" htmlFor={`opt-${kind}-${i}`}>{t('menu.available')}</label>
          </div>
          <button type="button" className="btn btn-outline-danger btn-sm" aria-label={t('options.removeRow', { kind: title, n: i + 1 })}
            onClick={() => setRows(rows.filter((_, n) => n !== i))}><Icon name="trash-can" /></button>
        </div>
      ))}
      <div>
        <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setRows([...rows, blankRow()])}>
          {t(`options.add${kind === 'variant' ? 'Size' : 'Extra'}`)}
        </button>
      </div>
    </div>
  );
}

// Owner and manager: the sizes and extras of one menu item.
export default function MenuOptionsDialog({ item, onClose }) {
  const t = useT();
  const { showToast } = useToast();
  const [variants, setVariants] = useState([]);
  const [addOns, setAddOns] = useState([]);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const tRef = useRef(t);
  tRef.current = t;

  useEffect(() => {
    if (!item) return undefined;
    let alive = true;
    setError(null);
    setVariants([]);
    setAddOns([]);
    (async () => {
      try {
        const res = await fetch(`/api/admin/menu/items/${item.id}/options`);
        if (!res.ok) throw new Error('load');
        const data = await res.json();
        if (!alive) return;
        setVariants(toRows(data.variants));
        setAddOns(toRows(data.addOns));
      } catch {
        if (alive) setError(tRef.current('common.error'));
      }
    })();
    return () => {
      alive = false;
    };
  }, [item]);

  if (!item) return null;

  const save = async () => {
    const all = [...variants, ...addOns];
    if (all.some((r) => r.name.trim() === '')) { setError(t('options.needName')); return; }
    if (all.some((r) => !validPrice(r.price))) { setError(t('options.needPrice')); return; }
    setBusy(true);
    setError(null);
    try {
      const res = await send(`/api/admin/menu/items/${item.id}/options`, 'PUT', { variants: toBody(variants), addOns: toBody(addOns) });
      if (res.ok) {
        showToast(t('options.saved', { name: item.name }));
        onClose(true);
      } else {
        const errors = await readErrors(res);
        setError([errors.variants, errors.addons, errors.general].filter(Boolean).join(' ') || t('common.error'));
      }
    } catch {
      setError(t('common.error'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <FormDialog open size="wide" title={t('options.title', { name: item.name })} submitLabel={t('staff.save')} busy={busy} error={error}
      onCancel={() => onClose(false)} onSubmit={save}>
      <OptionRows kind="variant" title={t('options.sizes')} hint={t('options.sizesHint')} rows={variants} setRows={setVariants} />
      <OptionRows kind="addOn" title={t('options.extras')} hint={t('options.extrasHint')} rows={addOns} setRows={setAddOns} />
    </FormDialog>
  );
}
