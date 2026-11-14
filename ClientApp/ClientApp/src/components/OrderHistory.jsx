import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import { formatMoney, parseServerDate } from '../ui/format';
import './OrderHistory.css';

// Works with both item shapes: items (itemName, unitPrice) and menuItems (name, price)
const getItems = (order) =>
  (order.items ?? order.menuItems ?? []).map((i) => ({
    name: i.itemName ?? i.name,
    quantity: i.quantity ?? 1,
    price: Number(i.unitPrice ?? i.price ?? 0),
  }));

const OrderHistory = () => {
  const t = useT();
  const [orders, setOrders] = useState([]);
  const [loadState, setLoadState] = useState('loading');

  const load = useCallback(async () => {
    setLoadState('loading');
    try {
      const res = await fetch('/api/orders');
      if (!res.ok) throw new Error('load failed');
      setOrders(await res.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const sorted = [...orders].sort(
    (a, b) => (parseServerDate(b.createdAt)?.getTime() ?? 0) - (parseServerDate(a.createdAt)?.getTime() ?? 0),
  );

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('orders.title')}</h1>
        <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={load}>{t('orders.refresh')}</button>
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
                <th scope="col">{t('orders.table')}</th>
                <th scope="col">{t('orders.status')}</th>
                <th scope="col">{t('orders.time')}</th>
                <th scope="col">{t('orders.items')}</th>
                <th scope="col" className="text-end">{t('orders.total')}</th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((order) => {
                const items = getItems(order);
                const total = items.reduce((sum, i) => sum + i.price * i.quantity, 0);
                const created = parseServerDate(order.createdAt);
                return (
                  <tr key={order.id}>
                    <td className="fw-bold">{order.tokenNumber}</td>
                    <td>{order.table?.tableNumber ?? order.tableId}</td>
                    <td>
                      <span className={`pos-status pos-status-${String(order.status).toLowerCase()}`}>{t(`status.${order.status}`)}</span>
                    </td>
                    <td className="text-nowrap">{created ? created.toLocaleString() : ''}</td>
                    <td>{items.map((i) => `${i.quantity} x ${i.name}`).join(', ')}</td>
                    <td className="text-end fw-semibold text-nowrap">{formatMoney(total)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </main>
  );
};

export default OrderHistory;
