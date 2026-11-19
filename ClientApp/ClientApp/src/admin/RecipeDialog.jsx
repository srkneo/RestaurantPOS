import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import FormDialog from '../ui/FormDialog';
import { readErrors, send } from '../ui/forms';

const blankRow = () => ({ stockItemId: '', quantity: '' });

// Owner and manager: how much stock one portion of a menu item uses. Completing an order takes it from stock.
export default function RecipeDialog({ item, onClose }) {
  const t = useT();
  const { showToast } = useToast();
  const [stock, setStock] = useState(null);
  const [rows, setRows] = useState([]);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!item) return undefined;
    let alive = true;
    setStock(null);
    setRows([]);
    setError(null);
    Promise.all([fetch('/api/stock'), fetch(`/api/menu/${item.id}/recipe`)])
      .then(async ([s, r]) => {
        if (!s.ok || !r.ok) throw new Error('load');
        const [stockList, recipe] = [await s.json(), await r.json()];
        if (!alive) return;
        setStock(stockList);
        setRows(recipe.length > 0 ? recipe.map((l) => ({ stockItemId: String(l.stockItemId), quantity: String(l.quantity) })) : [blankRow()]);
      })
      .catch(() => { if (alive) { setStock([]); setError(''); } });
    return () => { alive = false; };
  }, [item]);

  if (!item) return null;

  const update = (index, change) => setRows(rows.map((r, n) => (n === index ? { ...r, ...change } : r)));
  const unitOf = (id) => stock?.find((s) => String(s.id) === String(id))?.unit ?? '';

  const save = async () => {
    const filled = rows.filter((r) => r.stockItemId !== '' || String(r.quantity).trim() !== '');
    const bad = filled.some((r) => r.stockItemId === '' || !(Number(String(r.quantity).replace(/,/g, '')) > 0));
    if (bad) { setError(t('recipe.incomplete')); return; }
    setBusy(true);
    setError(null);
    try {
      const res = await send(`/api/menu/${item.id}/recipe`, 'PUT', {
        lines: filled.map((r) => ({ stockItemId: Number(r.stockItemId), quantity: Number(String(r.quantity).replace(/,/g, '')) })),
      });
      if (!res.ok) {
        const errs = await readErrors(res);
        setError(errs.lines || errs.general || '');
        return;
      }
      showToast(t('recipe.saved', { name: item.name }));
      onClose();
    } catch {
      setError(t('pos.networkError'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <FormDialog open title={t('recipe.title', { name: item.name })} submitLabel={t('recipe.save')} busy={busy || stock === null}
      error={error === null ? undefined : (error || t('common.error'))} onCancel={onClose} onSubmit={save}>
      <p className="small text-body-secondary mb-0">{t('recipe.hint')}</p>
      {stock !== null && stock.length === 0 && error === null && <p className="mb-0">{t('recipe.noStock')} <a href="/stock">{t('nav.stock')}</a></p>}
      {stock !== null && stock.length > 0 && (
        <div className="d-flex flex-column gap-2">
          {rows.map((r, i) => (
            <div key={i} className="d-flex flex-wrap align-items-center gap-2">
              <select className="form-select w-auto flex-grow-1" value={r.stockItemId} aria-label={t('recipe.rowItem', { n: i + 1 })}
                onChange={(e) => update(i, { stockItemId: e.target.value })}>
                <option value="">{t('recipe.choose')}</option>
                {stock.map((s) => <option key={s.id} value={String(s.id)}>{s.name}</option>)}
              </select>
              <input className="form-control w-auto" size={7} inputMode="decimal" value={r.quantity} aria-label={t('recipe.rowQty', { n: i + 1 })}
                onChange={(e) => update(i, { quantity: e.target.value })} />
              <span className="text-body-secondary">{unitOf(r.stockItemId)}</span>
              <button type="button" className="btn btn-outline-danger btn-sm" aria-label={t('recipe.removeRow', { n: i + 1 })}
                onClick={() => setRows(rows.filter((_, n) => n !== i))}>×</button>
            </div>
          ))}
          <div>
            <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setRows([...rows, blankRow()])}>{t('recipe.addRow')}</button>
          </div>
        </div>
      )}
    </FormDialog>
  );
}
