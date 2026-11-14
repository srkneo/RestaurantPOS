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
    connection.on('OrderStatusChanged', (data) => {
      if (!ACTIVE.includes(data.status)) setOrders((prev) => prev.filter((o) => o.id !== data.orderId));
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
  }, [fetchOrders]);

  // Refresh the wait times every 30 seconds.
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(id);
  }, []);

  const markReady = async (order) => {
    setBusyId(order.id);
    try {
      const res = await fetch(`/api/orders/${order.id}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status: 'Ready' }),
      });
      if (!res.ok) throw new Error('update failed');
      setOrders((prev) => prev.filter((o) => o.id !== order.id));
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
          return (
            <article key={order.id} className={`kds-ticket kds-${stage}`} aria-label={t('kitchen.ticket', { token: order.tokenNumber })}>
              <div className="kds-ticket-head">
                <div>
                  <div className="kds-token">{order.tokenNumber}</div>
                  <div className="kds-table">{t('pos.tableName', { number: order.table?.tableNumber ?? order.tableId })}</div>
                </div>
                <span className="kds-stage">{t(`kitchen.${stage}`, { minutes })}</span>
              </div>
              <ul className="kds-items">
                {(order.items ?? []).map((item, i) => (
                  <li key={item.id ?? i}>{item.quantity} x {item.itemName ?? item.name}</li>
                ))}
              </ul>
              <button type="button" className="kds-ready" disabled={busyId === order.id} onClick={() => markReady(order)}>
                {t('kitchen.markReady')}
              </button>
            </article>
          );
        })}
      </div>
    </div>
  );
};

export default KitchenDisplay;
