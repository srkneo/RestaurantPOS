import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { LanguageSwitch, useT } from '../i18n/I18nContext';
import ItemOptionsDialog from './ItemOptionsDialog';
import StateMessage from '../ui/StateMessage';
import { formatMoney, newClientId } from '../ui/format';
import './GuestOrderPage.css';

const lineKey = (id, variantId, addOnIds) => `${id}|${variantId ?? ''}|${[...(addOnIds ?? [])].sort((a, b) => a - b).join(',')}`;

// Guest page behind a table's QR code (/t/<token>): no sign-in. Menu, cart, send the order, call a waiter or ask for the bill.
export default function GuestOrderPage() {
  const t = useT();
  const { token } = useParams();
  const [menu, setMenu] = useState(null);
  const [state, setState] = useState('loading'); // loading | ready | notfound | error
  const [cart, setCart] = useState([]);
  const [choosing, setChoosing] = useState(null);
  const [sending, setSending] = useState(false);
  const [message, setMessage] = useState(null); // { kind: 'ok' | 'error', text }
  const [clientId, setClientId] = useState(newClientId);
  const [search, setSearch] = useState('');

  const load = useCallback(async () => {
    try {
      const res = await fetch(`/api/qr/${encodeURIComponent(token)}`);
      if (res.status === 404) { setState('notfound'); return; }
      if (!res.ok) throw new Error('load');
      setMenu(await res.json());
      setState('ready');
    } catch {
      setState('error');
    }
  }, [token]);

  useEffect(() => { load(); }, [load]);

  const groups = useMemo(() => {
    const byCategory = new Map();
    for (const item of menu?.items ?? []) {
      const key = item.category || '';
      if (!byCategory.has(key)) byCategory.set(key, []);
      byCategory.get(key).push(item);
    }
    return [...byCategory.entries()];
  }, [menu]);

  // Dish search: name, description or what a combo includes.
  const query = search.trim().toLowerCase();
  const shown = useMemo(() => (query
    ? groups
      .map(([category, items]) => [category, items.filter((i) => `${i.name} ${i.description ?? ''} ${i.includes ?? ''}`.toLowerCase().includes(query))])
      .filter(([, items]) => items.length > 0)
    : groups), [groups, query]);

  const jumpTo = (index) => {
    document.getElementById(`guest-cat-${index}`)?.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
  };

  const addLine = (line) => {
    const key = lineKey(line.id, line.variantId, line.addOnIds);
    setCart((prev) => {
      const found = prev.find((l) => l.key === key);
      if (found) return prev.map((l) => (l.key === key ? { ...l, quantity: Math.min(l.quantity + 1, 30) } : l));
      return [...prev, { variantId: null, addOnIds: [], options: null, ...line, key, quantity: 1 }];
    });
    setMessage(null);
  };

  const tap = (item) => {
    if ((item.variants?.length ?? 0) > 0 || (item.addOns?.length ?? 0) > 0) {
      setChoosing(item);
      return;
    }
    addLine({ id: item.id, name: item.name, price: Number(item.price) });
  };

  const change = (line, delta) => setCart((prev) => prev
    .map((l) => (l.key === line.key ? { ...l, quantity: l.quantity + delta } : l))
    .filter((l) => l.quantity > 0));

  const total = cart.reduce((sum, l) => sum + l.price * l.quantity, 0);

  const send = async () => {
    setSending(true);
    setMessage(null);
    try {
      const res = await fetch(`/api/qr/${encodeURIComponent(token)}/order`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          clientOrderId: clientId,
          items: cart.map((l) => ({
            menuItemId: l.id, quantity: l.quantity,
            ...(l.variantId ? { variantId: l.variantId } : {}),
            ...(l.addOnIds?.length ? { addOnIds: l.addOnIds } : {}),
          })),
        }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setMessage({ kind: 'error', text: data.message || t('qr.failed') });
        return;
      }
      setMessage({ kind: 'ok', text: t('qr.sent', { token: data.tokenNumber }) });
      setCart([]);
      setClientId(newClientId());
    } catch {
      setMessage({ kind: 'error', text: t('qr.failed') });
    } finally {
      setSending(false);
    }
  };

  const call = async (kind) => {
    try {
      const res = await fetch(`/api/qr/${encodeURIComponent(token)}/call`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ kind }),
      });
      const data = await res.json().catch(() => ({}));
      setMessage(res.ok ? { kind: 'ok', text: data.message } : { kind: 'error', text: data.message || t('qr.failed') });
    } catch {
      setMessage({ kind: 'error', text: t('qr.failed') });
    }
  };

  if (state === 'loading') return <StateMessage kind="loading" />;
  if (state === 'notfound') return <main className="guest"><p className="guest-note">{t('qr.notFound')}</p></main>;
  if (state === 'error') return <StateMessage kind="error" onRetry={load} />;

  return (
    <main className="guest">
      <header className="guest-head">
        <div>
          <h1>{menu.restaurantName || menu.outletName}</h1>
          <div className="guest-table">{t('qr.table', { number: menu.tableNumber })}</div>
        </div>
        <LanguageSwitch />
      </header>

      <div className="guest-calls">
        <button type="button" className="btn btn-outline-secondary" onClick={() => call('waiter')}>{t('qr.callWaiter')}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={() => call('bill')}>{t('qr.askBill')}</button>
      </div>
      {!menu.ordering && <p className="guest-note">{t('qr.off')}</p>}
      {message && <p className={`guest-message ${message.kind}`} role="status">{message.text}</p>}

      <div className="guest-find">
        <input type="search" className="form-control" aria-label={t('qr.search')} placeholder={t('qr.search')}
          value={search} onChange={(e) => setSearch(e.target.value)} />
        {!query && groups.length > 1 && (
          <nav className="guest-cats" aria-label={t('qr.categories')}>
            {groups.map(([category], i) => (
              <button key={category} type="button" className="guest-cat" onClick={() => jumpTo(i)}>{category || t('qr.menu')}</button>
            ))}
          </nav>
        )}
      </div>
      {query && shown.length === 0 && <p className="guest-note">{t('qr.noMatch')}</p>}

      {shown.map(([category, items], i) => (
        <section key={category} id={`guest-cat-${i}`} className="guest-section" aria-label={category || t('qr.menu')}>
          {category && <h2>{category}</h2>}
          <ul className="guest-items">
            {items.map((item) => (
              <li key={item.id}>
                <div>
                  <div className="guest-name">{item.name}</div>
                  {item.includes && <div className="guest-desc">{item.includes.split(', ').join(' · ')}</div>}
                  {item.description && <div className="guest-desc">{item.description}</div>}
                  <div className="guest-price">{formatMoney(item.price)}</div>
                </div>
                {menu.ordering && (
                  <button type="button" className="btn btn-primary guest-add" aria-label={t('qr.add', { name: item.name })} onClick={() => tap(item)}>+</button>
                )}
              </li>
            ))}
          </ul>
        </section>
      ))}

      {menu.ordering && cart.length > 0 && (
        <section className="guest-cart" aria-label={t('qr.cart')}>
          <h2>{t('qr.cart')}</h2>
          {cart.map((l) => (
            <div key={l.key} className="guest-line">
              <span>{l.name}{l.options ? ` + ${l.options}` : ''}</span>
              <span className="guest-qty">
                <button type="button" className="btn btn-outline-secondary btn-sm" aria-label={t('qr.less', { name: l.name })} onClick={() => change(l, -1)}>−</button>
                <span>{l.quantity}</span>
                <button type="button" className="btn btn-outline-secondary btn-sm" aria-label={t('qr.more', { name: l.name })} onClick={() => change(l, 1)}>+</button>
              </span>
            </div>
          ))}
          <div className="guest-total">{t('qr.total', { total: formatMoney(total) })}</div>
          <button type="button" className="btn btn-primary w-100" disabled={sending} onClick={send}>{sending ? t('qr.sending') : t('qr.send')}</button>
        </section>
      )}

      <ItemOptionsDialog item={choosing} onCancel={() => setChoosing(null)} onAdd={(line) => { addLine(line); setChoosing(null); }} />
    </main>
  );
}
