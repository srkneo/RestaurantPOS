import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import FormDialog from '../ui/FormDialog';
import StateMessage from '../ui/StateMessage';
import { formatMoney, parseServerDate } from '../ui/format';
import './OrderHistory.css';

const REASONS = ['reasonCustomerLeft', 'reasonWrongOrder', 'reasonOutOfStock', 'reasonOther'];

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
                      {order.status === 'Received' && (
                        <div className="pos-row-actions">
                          <Link className="btn btn-outline-secondary btn-sm px-3" to={`/pos?edit=${order.id}`}>{t('orders.edit')}</Link>
                          <button type="button" className="btn btn-outline-danger btn-sm px-3" onClick={() => openCancel(order)}>{t('orders.cancel')}</button>
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
    </main>
  );
};

export default OrderHistory;
