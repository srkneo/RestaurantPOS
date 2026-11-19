import React, { useCallback, useEffect, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { LanguageSwitch, useT } from '../i18n/I18nContext';
import { parseServerDate } from '../ui/format';
import './TokenDisplay.css';

const COOKING = ['Received', 'Preparing'];
const SHOWN = [...COOKING, 'Ready'];
const byToken = (a, b) => a.tokenNumber - b.tokenNumber;
// A ready token leaves the screen when staff press Served / Collected or the bill is paid,
// and at the latest 15 minutes after it became ready (the customer has surely taken it by then).
export const READY_SHOWN_MS = 15 * 60 * 1000;

// Customer-facing screen (a TV near the counter): tokens being prepared and tokens ready to collect, updated live.
export default function TokenDisplay() {
  const t = useT();
  const [orders, setOrders] = useState([]);
  const [loaded, setLoaded] = useState(false);
  const [live, setLive] = useState(false);
  const [now, setNow] = useState(Date.now());

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(id);
  }, []);

  const fetchOrders = useCallback(async () => {
    try {
      const res = await fetch('/api/orders');
      if (!res.ok) return;
      const data = await res.json();
      setOrders(data.filter((o) => SHOWN.includes(o.status)).map(({ id, tokenNumber, status, readyAt }) => ({ id, tokenNumber, status, readyAt })));
      setLoaded(true);
    } catch {
      // Keeps the last board; reconnecting loads it again.
    }
  }, []);

  useEffect(() => {
    fetchOrders();
    const upsert = (id, tokenNumber, status) => setOrders((prev) => {
      const rest = prev.filter((o) => o.id !== id);
      const readyAt = status === 'Ready' ? new Date().toISOString() : null;
      return SHOWN.includes(status) ? [...rest, { id, tokenNumber, status, readyAt }] : rest;
    });
    const connection = new signalR.HubConnectionBuilder().withUrl('/orderHub').withAutomaticReconnect().build();
    connection.on('ReceiveNewOrder', (o) => upsert(o.id, o.tokenNumber, o.status ?? 'Received'));
    connection.on('OrderUpdated', (o) => upsert(o.id, o.tokenNumber, o.status));
    connection.on('OrderStatusChanged', (d) => upsert(d.orderId, d.tokenNumber, d.status));
    connection.onreconnecting(() => setLive(false));
    connection.onreconnected(() => {
      setLive(true);
      fetchOrders();
    });
    connection.onclose(() => setLive(false));
    const started = connection.start().then(() => setLive(true)).catch(() => setLive(false));
    return () => {
      started.then(() => connection.stop());
    };
  }, [fetchOrders]);

  const cooking = orders.filter((o) => COOKING.includes(o.status)).sort(byToken);
  const ready = orders
    .filter((o) => o.status === 'Ready' && !(o.readyAt && now - parseServerDate(o.readyAt).getTime() > READY_SHOWN_MS))
    .sort(byToken);

  return (
    <div className="tokens">
      <header className="tokens-head">
        <h1>{t('tokens.title')}</h1>
        <span className={`tokens-live${live ? '' : ' off'}`}>
          <span className="tokens-dot" aria-hidden="true" />
          {live ? t('tokens.live') : t('tokens.offline')}
        </span>
        <LanguageSwitch />
      </header>
      <div className="tokens-board">
        <section className="tokens-col" aria-labelledby="tokens-cooking">
          <h2 id="tokens-cooking">{t('tokens.preparing')}</h2>
          <ul>{cooking.map((o) => <li key={o.id}>{o.tokenNumber}</li>)}</ul>
        </section>
        <section className="tokens-col tokens-ready" aria-labelledby="tokens-ready">
          <h2 id="tokens-ready">{t('tokens.ready')}</h2>
          <ul>{ready.map((o) => <li key={o.id}>{o.tokenNumber}</li>)}</ul>
        </section>
      </div>
      {loaded && orders.length === 0 && <p className="tokens-empty">{t('tokens.empty')}</p>}
    </div>
  );
}
