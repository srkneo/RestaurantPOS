import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import FormDialog from '../ui/FormDialog';
import BillDialog from './BillDialog';
import StateMessage from '../ui/StateMessage';
import { formatMoney, parseServerDate } from '../ui/format';
import './OrderHistory.css';

const REASONS = ['reasonCustomerLeft', 'reasonWrongOrder', 'reasonOutOfStock', 'reasonOther'];
const OPEN = ['Received', 'Preparing', 'Ready'];

// Works with both item shapes: items (itemName, unitPrice) and menuItems (name, price)
const getItems = (order) =>
  (order.items ?? order.menuItems ?? []).map((i) => ({
    name: i.itemName ?? i.name,
    quantity: i.quantity ?? 1,
    price: Number(i.unitPrice ?? i.price ?? 0),
    note: i.note,
  }));

const OrderHistory = () => {
  const t = useT();
  const { showToast } = useToast();
  const [orders, setOrders] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [date, setDate] = useState('');
  const [cancelling, setCancelling] = useState(null);
  const [reason, setReason] = useState('');
  const [otherReason, setOtherReason] = useState('');
  const [cancelError, setCancelError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [billOrderId, setBillOrderId] = useState(null);
  const [moving, setMoving] = useState(null);
  const [moveTables, setMoveTables] = useState([]);
  const [moveTo, setMoveTo] = useState('');
  const [moveError, setMoveError] = useState(null);
  const [merging, setMerging] = useState(null);
  const [mergeFrom, setMergeFrom] = useState('');
  const [splitting, setSplitting] = useState(null);
  const [splitQty, setSplitQty] = useState({});
  const [actionError, setActionError] = useState(null);

  const load = useCallback(async () => {
    setLoadState('loading');
    try {
      const res = await fetch(date ? `/api/orders?date=${date}` : '/api/orders');
      if (!res.ok) throw new Error('load failed');
      setOrders(await res.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, [date]);

  useEffect(() => {
    load();
  }, [load]);

  // Ready food went to the table (Served) or the customer took it (Collected): the order closes,
  // leaves the token screen and frees its table.
  const markDone = async (order) => {
    try {
      const res = await fetch(`/api/orders/${order.id}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status: 'Completed', version: order.version }),
      });
      if (!res.ok) throw new Error('failed');
      showToast(t('orders.doneToast', { token: order.tokenNumber }));
    } catch {
      showToast(t('common.error'), 'error');
    }
    load();
  };

  const openCancel = (order) => {
    setCancelling(order);
    setReason('');
    setOtherReason('');
    setCancelError(null);
  };

  const confirmCancel = async () => {
    const text = reason === 'reasonOther' ? otherReason.trim() : reason ? t(`orders.${reason}`) : '';
    if (!text) {
      setCancelError(t('orders.reasonRequired'));
      return;
    }
    setBusy(true);
    try {
      const res = await fetch(`/api/orders/${cancelling.id}/cancel`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason: text, version: cancelling.version }),
      });
      const data = await res.json().catch(() => ({}));
      if (res.status === 409) {
        setCancelError(data.code === 'cooking-started' ? t('pos.cannotEdit') : t('pos.conflict'));
        load();
        return;
      }
      if (!res.ok) {
        setCancelError(data.message || t('common.error'));
        return;
      }
      showToast(t('orders.cancelledToast', { token: cancelling.tokenNumber }));
      setCancelling(null);
      load();
    } catch {
      setCancelError(t('pos.networkError'));
    } finally {
      setBusy(false);
    }
  };

  // Guests changed tables: only free tables are offered.
  const openMove = async (order) => {
    setMoving(order);
    setMoveTo('');
    setMoveError(null);
    setMoveTables([]);
    try {
      const res = await fetch('/api/tables');
      if (!res.ok) throw new Error('load failed');
      const list = await res.json();
      setMoveTables(list.filter((tb) => tb.status === 'Free' && tb.id !== order.tableId));
    } catch {
      setMoveError(t('common.error'));
    }
  };

  const confirmMove = async () => {
    if (!moveTo) {
      setMoveError(t('orders.moveChoose'));
      return;
    }
    setBusy(true);
    try {
      const res = await fetch(`/api/orders/${moving.id}/move`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tableId: Number(moveTo), version: moving.version }),
      });
      const data = await res.json().catch(() => ({}));
      if (res.status === 409) {
        setMoveError(t('pos.conflict'));
        load();
        return;
      }
      if (!res.ok) {
        setMoveError(data.message || t('common.error'));
        return;
      }
      showToast(t('orders.moved', { token: moving.tokenNumber, number: data.table?.tableNumber ?? '' }));
      setMoving(null);
      load();
    } catch {
      setMoveError(t('pos.networkError'));
    } finally {
      setBusy(false);
    }
  };

  // Merge and split: same error handling as the other order changes.
  const postChange = async (url, body, done) => {
    setBusy(true);
    try {
      const res = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      const data = await res.json().catch(() => ({}));
      if (res.status === 409) {
        setActionError(data.code === 'billed' ? t('orders.billedFirst') : t('pos.conflict'));
        load();
        return;
      }
      if (!res.ok) {
        setActionError(data.message || t('common.error'));
        return;
      }
      done(data);
      load();
    } catch {
      setActionError(t('pos.networkError'));
    } finally {
      setBusy(false);
    }
  };

  const mergeChoices = merging
    ? orders.filter((o) => o.id !== merging.id && o.orderType !== 'Takeaway' && OPEN.includes(o.status))
    : [];

  const confirmMerge = () => {
    const from = orders.find((o) => String(o.id) === mergeFrom);
    if (!from) {
      setActionError(t('orders.mergeChoose'));
      return;
    }
    postChange(`/api/orders/${merging.id}/merge`, { fromOrderId: from.id, version: merging.version, fromVersion: from.version }, () => {
      showToast(t('orders.merged', { from: from.tokenNumber, token: merging.tokenNumber }));
      setMerging(null);
    });
  };

  const confirmSplit = () => {
    const lines = Object.entries(splitQty)
      .map(([id, q]) => ({ orderItemId: Number(id), quantity: Number(q) || 0 }))
      .filter((l) => l.quantity > 0);
    if (lines.length === 0) {
      setActionError(t('orders.splitChoose'));
      return;
    }
    postChange(`/api/orders/${splitting.id}/split`, { lines, version: splitting.version }, (data) => {
      showToast(t('orders.splitDone', { token: data.tokenNumber }));
      setSplitting(null);
    });
  };

  const sorted = [...orders].sort(
    (a, b) => (parseServerDate(b.createdAt)?.getTime() ?? 0) - (parseServerDate(a.createdAt)?.getTime() ?? 0),
  );

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('orders.title')}</h1>
        <div className="d-flex align-items-end gap-2">
          <div>
            <label htmlFor="orders-date" className="form-label">{t('orders.date')}</label>
            <input id="orders-date" type="date" className="form-control" value={date} onChange={(e) => setDate(e.target.value)} />
          </div>
          <button type="button" className="btn btn-outline-secondary px-3" onClick={load}>{t('orders.refresh')}</button>
        </div>
      </div>

      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && sorted.length === 0 && <StateMessage kind="empty" message={t('orders.empty')} />}
      {loadState === 'ready' && sorted.length > 0 && (
        <div className="pos-card table-responsive">
          <table className="table align-middle">
            <thead>
              <tr>
                <th scope="col">{t('orders.token')}</th>
                <th scope="col">{t('orders.type')}</th>
                <th scope="col">{t('orders.status')}</th>
                <th scope="col">{t('orders.time')}</th>
                <th scope="col">{t('orders.items')}</th>
                <th scope="col" className="text-end">{t('orders.total')}</th>
                <th scope="col"><span className="visually-hidden">{t('staff.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((order) => {
                const items = getItems(order);
                const total = items.reduce((sum, i) => sum + i.price * i.quantity, 0);
                const created = parseServerDate(order.createdAt);
                const where = order.orderType === 'Takeaway'
                  ? t('pos.takeawayName', { name: order.customerName || '' })
                  : t('pos.tableName', { number: order.table?.tableNumber ?? order.tableId });
                return (
                  <tr key={order.id}>
                    <td className="fw-bold">{order.tokenNumber}</td>
                    <td className="text-nowrap">{where}</td>
                    <td>
                      <span className={`pos-status pos-status-${String(order.status).toLowerCase()}`}>{t(`status.${order.status}`)}</span>
                      {order.cancelReason && <div className="pos-hint">{order.cancelReason}</div>}
                    </td>
                    <td className="text-nowrap">{created ? created.toLocaleString() : ''}</td>
                    <td>{items.map((i) => `${i.quantity} x ${i.name}${i.note ? ` (${i.note})` : ''}`).join(', ')}</td>
                    <td className="text-end fw-semibold text-nowrap">{formatMoney(total)}</td>
                    <td>
                      {order.status !== 'Cancelled' && (
                        <div className="pos-row-actions mb-1">
                          <button type="button" className="btn btn-primary btn-sm px-3" onClick={() => setBillOrderId(order.id)}>{t('bill.bill')}</button>
                          <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => window.open(`/print/kot/${order.id}?auto=1`, '_blank', 'noopener')}>{t('bill.kot')}</button>
                        </div>
                      )}
                      {order.status === 'Received' && (
                        <div className="pos-row-actions">
                          <Link className="btn btn-outline-secondary btn-sm px-3" to={`/pos?edit=${order.id}`}>{t('orders.edit')}</Link>
                          <button type="button" className="btn btn-outline-danger btn-sm px-3" onClick={() => openCancel(order)}>{t('orders.cancel')}</button>
                        </div>
                      )}
                      {order.status === 'Ready' && (
                        <div className="pos-row-actions mb-1">
                          <button type="button" className="btn btn-success btn-sm px-3" onClick={() => markDone(order)}>
                            {order.orderType === 'DineIn' ? t('orders.served') : t('orders.collected')}
                          </button>
                        </div>
                      )}
                      {order.orderType !== 'Takeaway' && ['Received', 'Preparing', 'Ready'].includes(order.status) && (
                        <div className="pos-row-actions mt-1">
                          <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => openMove(order)}>{t('orders.move')}</button>
                          <button type="button" className="btn btn-outline-secondary btn-sm px-3"
                            onClick={() => { setMerging(order); setMergeFrom(''); setActionError(null); }}>{t('orders.merge')}</button>
                          <button type="button" className="btn btn-outline-secondary btn-sm px-3"
                            onClick={() => { setSplitting(order); setSplitQty({}); setActionError(null); }}>{t('orders.split')}</button>
                        </div>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <FormDialog open={!!cancelling} title={t('orders.cancelTitle', { token: cancelling?.tokenNumber ?? '' })} submitLabel={t('orders.cancelConfirm')}
        busy={busy} error={cancelError} onCancel={() => setCancelling(null)} onSubmit={confirmCancel}>
        <div>
          <label htmlFor="cancel-reason" className="form-label">{t('orders.cancelReason')}</label>
          <select id="cancel-reason" className="form-select" value={reason} onChange={(e) => setReason(e.target.value)}>
            <option value="">{t('orders.reasonRequired')}</option>
            {REASONS.map((r) => <option key={r} value={r}>{t(`orders.${r}`)}</option>)}
          </select>
        </div>
        {reason === 'reasonOther' && (
          <div>
            <label htmlFor="cancel-other" className="form-label">{t('orders.otherReason')}</label>
            <input id="cancel-other" className="form-control" maxLength={200} value={otherReason} onChange={(e) => setOtherReason(e.target.value)} />
          </div>
        )}
      </FormDialog>
      <BillDialog orderId={billOrderId} onClose={() => setBillOrderId(null)} onChanged={load} />
      <FormDialog open={!!moving} title={t('orders.moveTitle', { token: moving?.tokenNumber ?? '' })} submitLabel={t('orders.moveConfirm')}
        busy={busy} error={moveError} onCancel={() => setMoving(null)} onSubmit={confirmMove}>
        <div>
          <label htmlFor="move-table" className="form-label">{t('orders.moveTo')}</label>
          <select id="move-table" className="form-select" value={moveTo} onChange={(e) => setMoveTo(e.target.value)}>
            <option value="">{t('orders.moveChoose')}</option>
            {moveTables.map((tb) => <option key={tb.id} value={tb.id}>{t('pos.tableName', { number: tb.tableNumber })}</option>)}
          </select>
        </div>
      </FormDialog>
      <FormDialog open={!!merging} title={t('orders.mergeTitle', { token: merging?.tokenNumber ?? '' })} submitLabel={t('orders.mergeConfirm')}
        busy={busy} error={actionError} onCancel={() => setMerging(null)} onSubmit={confirmMerge}>
        <div>
          <label htmlFor="merge-from" className="form-label">{t('orders.mergeFrom')}</label>
          <select id="merge-from" className="form-select" value={mergeFrom} onChange={(e) => setMergeFrom(e.target.value)}>
            <option value="">{t('orders.mergeChoose')}</option>
            {mergeChoices.map((o) => (
              <option key={o.id} value={o.id}>{t('orders.mergeOption', { token: o.tokenNumber, number: o.table?.tableNumber ?? o.tableId })}</option>
            ))}
          </select>
        </div>
        <p className="pos-hint mb-0">{t('orders.mergeHint')}</p>
      </FormDialog>
      <FormDialog open={!!splitting} title={t('orders.splitTitle', { token: splitting?.tokenNumber ?? '' })} submitLabel={t('orders.splitConfirm')}
        busy={busy} error={actionError} onCancel={() => setSplitting(null)} onSubmit={confirmSplit}>
        <p className="pos-hint mb-0">{t('orders.splitHint')}</p>
        {(splitting?.items ?? []).map((i) => (
          <div key={i.id} className="d-flex align-items-center justify-content-between gap-3">
            <label htmlFor={`split-${i.id}`} className="mb-0">{`${i.quantity} x ${i.itemName}`}</label>
            <input id={`split-${i.id}`} type="number" min="0" max={i.quantity} inputMode="numeric" className="form-control" style={{ maxWidth: 90 }}
              value={splitQty[i.id] ?? '0'} onChange={(e) => setSplitQty((q) => ({ ...q, [i.id]: e.target.value }))} />
          </div>
        ))}
      </FormDialog>
    </main>
  );
};

export default OrderHistory;
