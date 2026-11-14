import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import ConfirmDialog from '../ui/ConfirmDialog';
import StateMessage from '../ui/StateMessage';
import { formatMoney, newClientId } from '../ui/format';
import './POSMenu.css';

const MAX_QTY = 99;
const ALL = '__all__';

const POSMenu = () => {
  const t = useT();
  const { showToast } = useToast();
  const [menuItems, setMenuItems] = useState([]);
  const [tables, setTables] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [cart, setCart] = useState([]);
  const [category, setCategory] = useState(ALL);
  const [search, setSearch] = useState('');
  const [selectedTable, setSelectedTable] = useState('');
  const [placing, setPlacing] = useState(false);
  const [confirmClear, setConfirmClear] = useState(false);
  const clientOrderId = useRef(newClientId());
  const tRef = useRef(t);

  useEffect(() => {
    tRef.current = t;
  }, [t]);

  const load = useCallback(async () => {
    setLoadState('loading');
    try {
      const [menuRes, tablesRes] = await Promise.all([fetch('/api/menuitems'), fetch('/api/tables')]);
      if (!menuRes.ok || !tablesRes.ok) throw new Error('load failed');
      setMenuItems(await menuRes.json());
      setTables(await tablesRes.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  // Tell the cashier when the kitchen marks an order ready.
  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder().withUrl('/orderHub').withAutomaticReconnect().build();
    connection.on('OrderStatusChanged', (data) => {
      if (data.status === 'Ready') showToast(tRef.current('pos.orderReady', { token: data.tokenNumber }));
    });
    const started = connection.start().catch((err) => console.error(err));
    return () => {
      started.then(() => connection.stop());
    };
  }, [showToast]);

  const changeQty = (item, delta) => {
    setCart((prev) => {
      const line = prev.find((l) => l.id === item.id);
      if (!line) {
        return delta > 0 ? [...prev, { id: item.id, name: item.name, price: Number(item.price), quantity: 1 }] : prev;
      }
      const quantity = line.quantity + delta;
      if (quantity <= 0) return prev.filter((l) => l.id !== item.id);
      if (quantity > MAX_QTY) return prev;
      return prev.map((l) => (l.id === item.id ? { ...l, quantity } : l));
    });
  };

  const placeOrder = async () => {
    if (!selectedTable) {
      showToast(t('pos.needTable'), 'error');
      return;
    }
    if (cart.length === 0) {
      showToast(t('pos.needItems'), 'error');
      return;
    }
    setPlacing(true);
    try {
      const res = await fetch('/api/orders', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          table: Number(selectedTable),
          items: cart.map((l) => ({ menuItemId: l.id, quantity: l.quantity })),
          clientOrderId: clientOrderId.current,
        }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        showToast(data.message || t('pos.orderFailed'), 'error');
        return;
      }
      showToast(t('pos.orderPlaced', { token: data.tokenNumber }));
      setCart([]);
      setSelectedTable('');
      clientOrderId.current = newClientId();
    } catch {
      showToast(t('pos.networkError'), 'error');
    } finally {
      setPlacing(false);
    }
  };

  const categories = useMemo(() => [ALL, ...new Set(menuItems.map((i) => i.category).filter(Boolean))], [menuItems]);
  const visibleItems = useMemo(() => {
    const q = search.trim().toLowerCase();
    return menuItems.filter((i) => (category === ALL || i.category === category) && (!q || i.name.toLowerCase().includes(q)));
  }, [menuItems, category, search]);

  const qtyOf = (id) => cart.find((l) => l.id === id)?.quantity ?? 0;
  const itemCount = cart.reduce((sum, l) => sum + l.quantity, 0);
  const total = cart.reduce((sum, l) => sum + l.price * l.quantity, 0);
  const tableNumber = tables.find((tb) => String(tb.id) === selectedTable)?.tableNumber;

  return (
    <div className="pos-screen">
      <main className="pos-menu">
        <div className="pos-filters">
          <div className="pos-field-table">
            <label htmlFor="pos-table" className="form-label">{t('pos.table')}</label>
            <select id="pos-table" className="form-select" value={selectedTable} onChange={(e) => setSelectedTable(e.target.value)}>
              <option value="">{t('pos.chooseTable')}</option>
              {tables.map((tb) => (
                <option key={tb.id} value={tb.id}>{t('pos.tableName', { number: tb.tableNumber })}</option>
              ))}
            </select>
          </div>
          <div className="pos-field-search">
            <label htmlFor="pos-search" className="form-label">{t('pos.search')}</label>
            <input id="pos-search" type="search" className="form-control" placeholder={t('pos.searchHint')}
              value={search} onChange={(e) => setSearch(e.target.value)} />
          </div>
        </div>

        {loadState === 'loading' && <StateMessage kind="loading" />}
        {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
        {loadState === 'ready' && (
          <>
            <div className="pos-chips" role="group" aria-label={t('pos.categories')}>
              {categories.map((c) => (
                <button key={c} type="button" className="pos-chip" aria-pressed={category === c} onClick={() => setCategory(c)}>
                  {c === ALL ? t('pos.all') : c}
                </button>
              ))}
            </div>
            {visibleItems.length === 0 ? (
              <StateMessage kind="empty" message={t('pos.noItems')} />
            ) : (
              <div className="pos-items">
                {visibleItems.map((item) => {
                  const qty = qtyOf(item.id);
                  return (
                    <button key={item.id} type="button" className={`pos-item${qty ? ' in-cart' : ''}`}
                      aria-label={t('pos.addItem', { name: item.name })} onClick={() => changeQty(item, 1)}>
                      <span className="pos-item-cat">{item.category}</span>
                      <span className="pos-item-name">{item.name}</span>
                      <span className="pos-item-foot">
                        {formatMoney(item.price)}
                        {qty > 0 && <span className="pos-badge">{t('pos.inCart', { count: qty })}</span>}
                      </span>
                    </button>
                  );
                })}
              </div>
            )}
          </>
        )}
      </main>

      <aside className="pos-order" aria-label={t('pos.currentOrder')}>
        <div className="pos-order-head">
          <h2>{t('pos.currentOrder')}</h2>
          {tableNumber && <span className="pos-order-table">{t('pos.tableName', { number: tableNumber })}</span>}
        </div>
        <div className="pos-order-lines">
          {cart.length === 0 ? (
            <p className="pos-empty-cart">{t('pos.emptyCart')}</p>
          ) : (
            cart.map((l) => (
              <div key={l.id} className="pos-line">
                <div className="pos-line-info">
                  <div className="pos-line-name">{l.name}</div>
                  <div className="pos-line-price">{formatMoney(l.price * l.quantity)}</div>
                </div>
                <button type="button" className="pos-qty-btn" aria-label={t('pos.oneLess', { name: l.name })} onClick={() => changeQty(l, -1)}>−</button>
                <span className="pos-qty">{l.quantity}</span>
                <button type="button" className="pos-qty-btn" aria-label={t('pos.oneMore', { name: l.name })} onClick={() => changeQty(l, 1)}>+</button>
              </div>
            ))
          )}
        </div>
        <div className="pos-total">
          <span className="fw-semibold">{t('pos.total', { count: itemCount })}</span>
          <span className="pos-total-amount">{formatMoney(total)}</span>
        </div>
        <button type="button" className="btn btn-primary pos-place" onClick={placeOrder} disabled={placing}>
          {placing ? t('pos.placing') : t('pos.placeOrder')}
        </button>
        <button type="button" className="btn btn-outline-secondary" onClick={() => setConfirmClear(true)} disabled={cart.length === 0}>
          {t('pos.clearOrder')}
        </button>
      </aside>

      <ConfirmDialog open={confirmClear} title={t('pos.clearTitle')} confirmLabel={t('pos.clearConfirm')} danger
        onCancel={() => setConfirmClear(false)}
        onConfirm={() => { setCart([]); setConfirmClear(false); }} />
    </div>
  );
};

export default POSMenu;
