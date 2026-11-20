import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useAuth } from '../AuthContext';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import FormDialog from '../ui/FormDialog';
import ConfirmDialog from '../ui/ConfirmDialog';
import { readErrors, send, Field } from '../ui/forms';
import { formatMoney, parseServerDate } from '../ui/format';
import './OrderHistory.css';

export const STOCK_UNITS = ['kg', 'g', 'l', 'ml', 'pcs', 'pack', 'box', 'dozen'];
export const STOCK_KINDS = ['Purchase', 'Used', 'Waste', 'Count'];
const BLANK_ITEM = { name: '', unit: 'kg', lowLevel: '', quantity: '' };
const BLANK_MOVE = { kind: 'Purchase', quantity: '', cost: '', note: '', supplier: '' };
const qty = (n) => Number(n ?? 0).toLocaleString('en-IN', { maximumFractionDigits: 3 });
const num = (v) => {
  const s = String(v ?? '').replace(/,/g, '').trim();
  return s === '' ? null : Number(s);
};

// Stock of this outlet (milk, paneer, oil, cups...). Cashier and kitchen record what was bought, used, wasted or
// counted; the owner and manager also add, edit and remove items. Items at or under their low level are marked.
export default function StockPage() {
  const t = useT();
  const { user } = useAuth();
  const { showToast } = useToast();
  const manager = ['admin', 'manager'].some((r) => user?.roles?.includes(r));
  const [items, setItems] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [search, setSearch] = useState('');
  const [onlyLow, setOnlyLow] = useState(false);
  const [editing, setEditing] = useState(null);
  const [itemForm, setItemForm] = useState(BLANK_ITEM);
  const [moving, setMoving] = useState(null);
  const [moveForm, setMoveForm] = useState(BLANK_MOVE);
  const [history, setHistory] = useState(null);
  const [removing, setRemoving] = useState(null);
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/stock');
      if (!res.ok) throw new Error('load');
      setItems(await res.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const shown = useMemo(() => {
    const q = search.trim().toLowerCase();
    return items.filter((i) => (!onlyLow || i.isLow) && (!q || i.name.toLowerCase().includes(q)));
  }, [items, search, onlyLow]);
  const lowCount = items.filter((i) => i.isLow).length;
  const generalError = errors.general !== undefined ? (errors.general || t('common.error')) : undefined;

  const setItem = (field) => (e) => setItemForm((f) => ({ ...f, [field]: e.target.value }));
  const setMove = (field) => (e) => setMoveForm((f) => ({ ...f, [field]: e.target.value }));

  const openItem = (item) => {
    setErrors({});
    setEditing(item ?? {});
    setItemForm(item ? { name: item.name, unit: item.unit, lowLevel: String(item.lowLevel), quantity: '' } : BLANK_ITEM);
  };

  const openMove = (item) => {
    setErrors({});
    setMoving(item);
    setMoveForm(BLANK_MOVE);
  };

  const saved = (item) => {
    showToast(t('stock.saved', { name: item.name, quantity: qty(item.quantity), unit: item.unit }));
    load();
  };

  const saveItem = async () => {
    const lowLevel = num(itemForm.lowLevel) ?? 0;
    const quantity = num(itemForm.quantity) ?? 0;
    if (Number.isNaN(lowLevel)) { setErrors({ lowlevel: t('stock.badQty') }); return; }
    if (!editing.id && Number.isNaN(quantity)) { setErrors({ quantity: t('stock.badQty') }); return; }
    const body = { name: itemForm.name.trim(), unit: itemForm.unit, lowLevel };
    if (!editing.id) body.quantity = quantity;
    setBusy(true);
    try {
      const res = await send(editing.id ? `/api/stock/${editing.id}` : '/api/stock', editing.id ? 'PUT' : 'POST', body);
      if (!res.ok) { setErrors(await readErrors(res)); return; }
      setEditing(null);
      saved(await res.json());
    } catch {
      setErrors({ general: t('pos.networkError') });
    } finally {
      setBusy(false);
    }
  };

  const saveMove = async () => {
    const quantity = num(moveForm.quantity);
    if (quantity === null || Number.isNaN(quantity)) { setErrors({ quantity: t('stock.badQty') }); return; }
    const cost = moveForm.kind === 'Purchase' ? num(moveForm.cost) : null;
    setBusy(true);
    try {
      const res = await send(`/api/stock/${moving.id}/moves`, 'POST', {
        kind: moveForm.kind, quantity, cost: Number.isNaN(cost) ? null : cost, note: moveForm.note.trim() || null,
        supplier: moveForm.kind === 'Purchase' ? (moveForm.supplier.trim() || null) : null,
      });
      if (!res.ok) { setErrors(await readErrors(res)); return; }
      setMoving(null);
      saved(await res.json());
    } catch {
      setErrors({ general: t('pos.networkError') });
    } finally {
      setBusy(false);
    }
  };

  const openHistory = async (item) => {
    setHistory({ item, moves: null });
    try {
      const res = await fetch(`/api/stock/${item.id}/moves`);
      setHistory({ item, moves: res.ok ? await res.json() : [] });
    } catch {
      setHistory({ item, moves: [] });
    }
  };

  const remove = async () => {
    const item = removing;
    setRemoving(null);
    const res = await send(`/api/stock/${item.id}`, 'DELETE').catch(() => null);
    if (res?.ok) {
      showToast(t('stock.removed', { name: item.name }));
      load();
    } else {
      showToast(t('common.error'));
    }
  };

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('stock.title')}</h1>
        {manager && (
          <div className="d-flex gap-2">
            <a href="/stock/report" className="btn btn-outline-secondary px-3">{t('stock.report')}</a>
            <button type="button" className="btn btn-primary px-3" onClick={() => openItem(null)}>{t('stock.add')}</button>
          </div>
        )}
      </div>
      {lowCount > 0 && <p className="alert alert-warning" role="status">{t('stock.lowCount', { count: lowCount })}</p>}
      <div className="d-flex flex-wrap gap-3 align-items-center mb-3">
        <input type="search" className="form-control w-auto" placeholder={t('stock.search')} aria-label={t('stock.search')}
          value={search} onChange={(e) => setSearch(e.target.value)} />
        <div className="form-check">
          <input id="stock-only-low" type="checkbox" className="form-check-input" checked={onlyLow} onChange={(e) => setOnlyLow(e.target.checked)} />
          <label htmlFor="stock-only-low" className="form-check-label">{t('stock.onlyLow')}</label>
        </div>
      </div>

      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && shown.length === 0 && <StateMessage kind="empty" message={t('stock.empty')} />}
      {loadState === 'ready' && shown.length > 0 && (
        <div className="pos-card table-responsive">
          <table className="table align-middle">
            <thead>
              <tr>
                <th scope="col">{t('stock.name')}</th>
                <th scope="col" className="text-end">{t('stock.quantity')}</th>
                <th scope="col" className="text-end">{t('stock.lowLevel')}</th>
                <th scope="col" className="text-end">{t('stock.cost')}</th>
                <th scope="col">{t('stock.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((i) => (
                <tr key={i.id}>
                  <td>{i.name} {i.isLow && <span className="badge text-bg-warning">{t('stock.low')}</span>}</td>
                  <td className="text-end fw-semibold text-nowrap">{qty(i.quantity)} {i.unit}</td>
                  <td className="text-end text-nowrap">{i.lowLevel > 0 ? `${qty(i.lowLevel)} ${i.unit}` : '–'}</td>
                  <td className="text-end text-nowrap">{i.costPerUnit != null ? formatMoney(i.costPerUnit) : '–'}</td>
                  <td>
                    <div className="d-flex flex-wrap gap-2">
                      <button type="button" className="btn btn-sm btn-primary" aria-label={t('stock.recordTitle', { name: i.name })} onClick={() => openMove(i)}>{t('stock.record')}</button>
                      <button type="button" className="btn btn-sm btn-outline-secondary" aria-label={t('stock.historyTitle', { name: i.name })} onClick={() => openHistory(i)}>{t('stock.history')}</button>
                      {manager && <button type="button" className="btn btn-sm btn-outline-secondary" aria-label={`${t('stock.editBtn')} ${i.name}`} onClick={() => openItem(i)}>{t('stock.editBtn')}</button>}
                      {manager && <button type="button" className="btn btn-sm btn-outline-danger" aria-label={`${t('stock.remove')} ${i.name}`} onClick={() => setRemoving(i)}>{t('stock.remove')}</button>}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <FormDialog open={editing !== null} title={editing?.id ? t('stock.edit') : t('stock.add')} submitLabel={t('stock.save')}
        busy={busy} error={generalError} onCancel={() => setEditing(null)} onSubmit={saveItem}>
        <Field id="stock-name" label={t('stock.name')} error={errors.name}>
          <input id="stock-name" className="form-control" maxLength={60} autoComplete="off" value={itemForm.name} onChange={setItem('name')} />
        </Field>
        <Field id="stock-unit" label={t('stock.unit')} error={errors.unit}>
          <select id="stock-unit" className="form-select" value={itemForm.unit} onChange={setItem('unit')}>
            {STOCK_UNITS.map((u) => <option key={u} value={u}>{u}</option>)}
          </select>
        </Field>
        {!editing?.id && (
          <Field id="stock-opening" label={t('stock.opening')} error={errors.quantity}>
            <input id="stock-opening" className="form-control" inputMode="decimal" autoComplete="off" value={itemForm.quantity} onChange={setItem('quantity')} />
          </Field>
        )}
        <Field id="stock-low" label={t('stock.lowLevel')} error={errors.lowlevel}>
          <input id="stock-low" className="form-control" inputMode="decimal" autoComplete="off" value={itemForm.lowLevel} onChange={setItem('lowLevel')} />
        </Field>
      </FormDialog>

      <FormDialog open={moving !== null} title={moving ? t('stock.recordTitle', { name: moving.name }) : ''} submitLabel={t('stock.save')}
        busy={busy} error={generalError} onCancel={() => setMoving(null)} onSubmit={saveMove}>
        <Field id="stock-kind" label={t('stock.kind')} error={errors.kind}>
          <select id="stock-kind" className="form-select" value={moveForm.kind} onChange={setMove('kind')}>
            {STOCK_KINDS.map((k) => <option key={k} value={k}>{t(`stock.kind.${k}`)}</option>)}
          </select>
        </Field>
        <Field id="stock-move-qty" label={t('stock.moveQty', { unit: moving?.unit ?? '' })} error={errors.quantity}>
          <input id="stock-move-qty" className="form-control" inputMode="decimal" autoComplete="off" value={moveForm.quantity} onChange={setMove('quantity')} />
        </Field>
        {moveForm.kind === 'Purchase' && (
          <Field id="stock-move-cost" label={t('stock.totalCost')} error={errors.cost}>
            <input id="stock-move-cost" className="form-control" inputMode="decimal" autoComplete="off" value={moveForm.cost} onChange={setMove('cost')} />
          </Field>
        )}
        {moveForm.kind === 'Purchase' && (
          <Field id="stock-move-supplier" label={t('stock.supplier')} error={errors.supplier}>
            <input id="stock-move-supplier" className="form-control" maxLength={60} autoComplete="off" value={moveForm.supplier} onChange={setMove('supplier')} />
          </Field>
        )}
        <Field id="stock-move-note" label={t('stock.note')} error={errors.note}>
          <input id="stock-move-note" className="form-control" maxLength={100} autoComplete="off" value={moveForm.note} onChange={setMove('note')} />
        </Field>
      </FormDialog>

      <FormDialog open={history !== null} title={history ? t('stock.historyTitle', { name: history.item.name }) : ''} submitLabel={t('common.close')}
        onCancel={() => setHistory(null)} onSubmit={() => setHistory(null)}>
        {history?.moves === null && <StateMessage kind="loading" />}
        {history?.moves?.length === 0 && <p className="mb-0">{t('stock.noHistory')}</p>}
        {history?.moves?.length > 0 && (
          <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead>
                <tr>
                  <th scope="col">{t('stock.when')}</th>
                  <th scope="col">{t('stock.kind')}</th>
                  <th scope="col" className="text-end">{t('stock.change')}</th>
                  <th scope="col" className="text-end">{t('stock.after')}</th>
                  <th scope="col">{t('stock.note')}</th>
                </tr>
              </thead>
              <tbody>
                {history.moves.map((m) => (
                  <tr key={m.id}>
                    <td className="text-nowrap">{parseServerDate(m.at)?.toLocaleString([], { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }) ?? ''}</td>
                    <td>{t(`stock.kind.${m.kind}`)}</td>
                    <td className="text-end text-nowrap">{m.change > 0 ? '+' : ''}{qty(m.change)}</td>
                    <td className="text-end text-nowrap">{qty(m.quantityAfter)} {history.item.unit}</td>
                    <td>{m.note ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </FormDialog>

      <ConfirmDialog open={removing !== null} title={removing ? t('stock.removeTitle', { name: removing.name }) : ''} message={t('stock.removeText')}
        confirmLabel={t('stock.remove')} danger onConfirm={remove} onCancel={() => setRemoving(null)} />
    </main>
  );
}
