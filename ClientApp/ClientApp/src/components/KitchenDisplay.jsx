import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../AuthContext';
import { LanguageSwitch, useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { kitchenStage, minutesSince, parseServerDate } from '../ui/format';
import './KitchenDisplay.css';

const ACTIVE = ['Received', 'Preparing'];
const POS_ROLES = ['cashier', 'manager', 'admin'];

const KitchenDisplay = () => {
  const t = useT();
  const auth = useAuth();
  const { showToast } = useToast();
  const [orders, setOrders] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [live, setLive] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const [busyId, setBusyId] = useState(null);
  const loaded = useRef(false);
  const tRef = useRef(t);
  const ordersRef = useRef([]);

  useEffect(() => {
    tRef.current = t;
  }, [t]);

  useEffect(() => {
    ordersRef.current = orders;
  }, [orders]);

  const fetchOrders = useCallback(async () => {
    try {
      const res = await fetch('/api/orders');
      if (!res.ok) throw new Error('load failed');
      const data = await res.json();
      setOrders(data.filter((o) => ACTIVE.includes(o.status)));
      loaded.current = true;
      setLoadState('ready');
    } catch {
      if (!loaded.current) setLoadState('error');
    }
  }, []);

  useEffect(() => {
    fetchOrders();
    const connection = new signalR.HubConnectionBuilder().withUrl('/orderHub').withAutomaticReconnect().build();
    connection.on('ReceiveNewOrder', (order) => {
      setOrders((prev) => (prev.some((o) => o.id === order.id)
        ? prev
        : [...prev, { ...order, createdAt: order.createdAt ?? new Date().toISOString() }]));
    });
    connection.on('OrderUpdated', (order) => {
      setOrders((prev) => prev.map((o) => (o.id === order.id ? { ...o, ...order } : o)));
    });
    connection.on('OrderStatusChanged', (data) => {
      if (data.status === 'Cancelled' && ordersRef.current.some((o) => o.id === data.orderId)) {
        showToast(tRef.current('kitchen.cancelled', { token: data.tokenNumber }), 'error');
      }
      if (ACTIVE.includes(data.status)) {
        setOrders((prev) => prev.map((o) => (o.id === data.orderId ? { ...o, status: data.status, version: data.version ?? o.version } : o)));
      } else {
        setOrders((prev) => prev.filter((o) => o.id !== data.orderId));
      }
    });
    connection.onreconnecting(() => setLive(false));
    connection.onreconnected(() => {
      setLive(true);
      fetchOrders();
    });
    connection.onclose(() => setLive(false));
    const started = connection.start()
      .then(() => setLive(true))
      .catch((err) => {
        console.error(err);
        setLive(false);
      });
    return () => {
      started.then(() => connection.stop());
    };
  }, [fetchOrders, showToast]);

  // Refresh the wait times every 30 seconds.
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(id);
  }, []);

  const setStatus = async (order, status) => {
    setBusyId(order.id);
    try {
      const res = await fetch(`/api/orders/${order.id}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status, version: order.version }),
      });
      if (res.status === 409) {
        await fetchOrders();
        throw new Error('changed');
      }
      if (!res.ok) throw new Error('update failed');
      setOrders((prev) => (status === 'Ready'
        ? prev.filter((o) => o.id !== order.id)
        : prev.map((o) => (o.id === order.id ? { ...o, status, version: Math.max(o.version ?? 0, (order.version ?? 0) + 1) } : o))));
    } catch {
      showToast(t('kitchen.markFailed', { token: order.tokenNumber }), 'error');
    } finally {
      setBusyId(null);
    }
  };

  const sorted = [...orders].sort(
    (a, b) => (parseServerDate(a.createdAt)?.getTime() ?? 0) - (parseServerDate(b.createdAt)?.getTime() ?? 0),
  );
  const user = auth?.user;
  const canUsePos = user && POS_ROLES.some((r) => user.roles.includes(r));

  return (
    <div className="kds">
      <header className="kds-header">
        <h1>{t('kitchen.title')}</h1>
        <span className="kds-count">{t('kitchen.waitingCount', { count: sorted.length })}</span>
        <span className={`kds-live${live ? '' : ' off'}`}>
          <span className="kds-dot" aria-hidden="true" />
          {live ? t('kitchen.live') : t('kitchen.offline')}
        </span>
        <LanguageSwitch />
        {canUsePos && <Link to="/pos" className="kds-back">{t('kitchen.backToPos')}</Link>}
      </header>

      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={fetchOrders} />}
      {loadState === 'ready' && sorted.length === 0 && <StateMessage kind="empty" message={t('kitchen.empty')} />}

      <div className="kds-grid">
        {sorted.map((order) => {
          const minutes = minutesSince(order.createdAt, now);
          const stage = kitchenStage(minutes);
          const cooking = order.status === 'Preparing';
          const where = order.orderType === 'Takeaway'
            ? t('pos.takeawayName', { name: order.customerName || '' })
            : t('pos.tableName', { number: order.table?.tableNumber ?? order.tableId });
          return (
            <article key={order.id} className={`kds-ticket kds-${stage}`} aria-label={t('kitchen.ticket', { token: order.tokenNumber })}>
              <div className="kds-ticket-head">
                <div>
                  <div className="kds-token">{order.tokenNumber}</div>
                  <div className={`kds-table${order.orderType === 'Takeaway' ? ' kds-takeaway' : ''}`}>{where}</div>
                </div>
                <div className="kds-tags">
                  <span className="kds-stage">{t(`kitchen.${stage}`, { minutes })}</span>
                  {cooking && <span className="kds-cooking">{t('kitchen.cooking')}</span>}
                </div>
              </div>
              <ul className="kds-items">
                {(order.items ?? []).map((item, i) => (
                  <li key={item.id ?? i}>
                    <span>{item.quantity} x {item.itemName ?? item.name}</span>
                    {item.note && <span className="kds-note">{item.note}</span>}
                  </li>
                ))}
              </ul>
              <div className="kds-actions">
                {!cooking && (
                  <button type="button" className="kds-start" disabled={busyId === order.id} onClick={() => setStatus(order, 'Preparing')}>
                    {t('kitchen.startCooking')}
                  </button>
                )}
                <button type="button" className="kds-ready" disabled={busyId === order.id} onClick={() => setStatus(order, 'Ready')}>
                  {t('kitchen.markReady')}
                </button>
              </div>
            </article>
          );
        })}
      </div>
    </div>
  );
};

export default KitchenDisplay;
