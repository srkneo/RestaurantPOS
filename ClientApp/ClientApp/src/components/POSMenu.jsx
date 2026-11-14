import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import * as signalR from '@microsoft/signalr';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import ConfirmDialog from '../ui/ConfirmDialog';
import StateMessage from '../ui/StateMessage';
import { formatMoney, newClientId } from '../ui/format';
import './POSMenu.css';

const MAX_QTY = 99;
const ALL = '__all__';

export function VegMark({ isVeg }) {
  const t = useT();
  const label = isVeg === false ? t('pos.nonVeg') : t('pos.veg');
  return <span className={`pos-veg ${isVeg === false ? 'nonveg' : 'veg'}`} role="img" aria-label={label} title={label} />;
}

const POSMenu = () => {
  const t = useT();
  const { showToast } = useToast();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const editId = searchParams.get('edit');

  const [menuItems, setMenuItems] = useState([]);
  const [tables, setTables] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [cart, setCart] = useState([]);
  const [category, setCategory] = useState(ALL);
  const [search, setSearch] = useState('');
  const [orderType, setOrderType] = useState('DineIn');
  const [selectedTable, setSelectedTable] = useState('');
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [noteOpen, setNoteOpen] = useState(null);
  const [placing, setPlacing] = useState(false);
  const [confirmClear, setConfirmClear] = useState(false);
  const [editing, setEditing] = useState(null); // { id, tokenNumber, version }
  const [orderSettings, setOrderSettings] = useState(null);
  const clientOrderId = useRef(newClientId());
  const tRef = useRef(t);
  // The cashier's own choice wins over the outlet default that arrives with the settings.
  const typeChosen = useRef(false);

  useEffect(() => {
    tRef.current = t;
  }, [t]);

  const load = useCallback(async () => {
    setLoadState('loading');
    try {
      const [menuRes, tablesRes, settingsRes] = await Promise.all([fetch('/api/menuitems'), fetch('/api/tables'), fetch('/api/store/settings')]);
      if (!menuRes.ok || !tablesRes.ok) throw new Error('load failed');
      setMenuItems(await menuRes.json());
      setTables(await tablesRes.json());
      // Outlet settings decide which order types the POS offers and which one it starts with.
      if (settingsRes?.ok) {
        const settings = await settingsRes.json();
        setOrderSettings(settings);
        if (!editId && !typeChosen.current && settings.defaultOrderType) setOrderType(settings.defaultOrderType);
      }
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, [editId]);

  useEffect(() => {
    load();
  }, [load]);

  const resetOrder = useCallback(() => {
    setCart([]);
    setSelectedTable('');
    setCustomerName('');
    setCustomerPhone('');
    setNoteOpen(null);
    clientOrderId.current = newClientId();
  }, []);

  // /pos?edit=12 loads order 12 so its items can be changed before cooking starts.
  useEffect(() => {
    if (!editId) {
      setEditing(null);
      return;
    }
    let active = true;
    (async () => {
      try {
        const res = await fetch(`/api/orders/${editId}`);
        if (!res.ok) throw new Error('not found');
        const order = await res.json();
        if (!active) return;
        if (order.status !== 'Received') {
          showToast(tRef.current('pos.cannotEdit'), 'error');
          navigate('/orders');
          return;
        }
        setEditing({ id: order.id, tokenNumber: order.tokenNumber, version: order.version });
        setOrderType(order.orderType || 'DineIn');
        setSelectedTable(order.tableId ? String(order.tableId) : '');
        setCustomerName(order.customerName || '');
        setCustomerPhone(order.customerPhone || '');
        setCart(order.items.map((i) => ({ id: i.menuItemId, name: i.itemName, price: Number(i.unitPrice), quantity: i.quantity, note: i.note || '' })));
      } catch {
        if (active) showToast(tRef.current('common.error'), 'error');
      }
    })();
    return () => {
      active = false;
    };
  }, [editId, navigate, showToast]);

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
        return delta > 0 ? [...prev, { id: item.id, name: item.name, price: Number(item.price), quantity: 1, note: '' }] : prev;
      }
      const quantity = line.quantity + delta;
      if (quantity <= 0) return prev.filter((l) => l.id !== item.id);
      if (quantity > MAX_QTY) return prev;
      return prev.map((l) => (l.id === item.id ? { ...l, quantity } : l));
    });
  };

  const setNote = (id, note) => setCart((prev) => prev.map((l) => (l.id === id ? { ...l, note } : l)));

  const lines = () => cart.map((l) => ({ menuItemId: l.id, quantity: l.quantity, note: l.note?.trim() || null }));

  const placeOrder = async () => {
    if (!editing && orderType === 'DineIn' && !selectedTable) {
      showToast(t('pos.needTable'), 'error');
      return;
    }
    if (!editing && orderType === 'Takeaway' && orderSettings?.takeawayNeedsName !== false && !customerName.trim()) {
      showToast(t('pos.needName'), 'error');
      return;
    }
    if (cart.length === 0) {
      showToast(t('pos.needItems'), 'error');
      return;
    }
    setPlacing(true);
    try {
      const res = editing
        ? await fetch(`/api/orders/${editing.id}/items`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ items: lines(), version: editing.version }),
        })
        : await fetch('/api/orders', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            orderType,
            table: orderType === 'DineIn' ? Number(selectedTable) : null,
            customerName: customerName.trim() || null,
            customerPhone: customerPhone.trim() || null,
            items: lines(),
            clientOrderId: clientOrderId.current,
          }),
        });
      const data = await res.json().catch(() => ({}));
      if (res.status === 409) {
        showToast(data.code === 'cooking-started' ? t('pos.cannotEdit') : t('pos.conflict'), 'error');
        return;
      }
      if (!res.ok) {
        showToast(data.message || t('pos.orderFailed'), 'error');
        return;
      }
      if (editing) {
        showToast(t('pos.orderUpdated', { token: editing.tokenNumber }));
        resetOrder();
        navigate('/orders');
      } else {
        showToast(t('pos.orderPlaced', { token: data.tokenNumber }));
        resetOrder();
      }
    } catch {
      showToast(t('pos.networkError'), 'error');
    } finally {
      setPlacing(false);
    }
  };

  const stopEditing = () => {
    resetOrder();
    navigate('/pos');
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
  const orderLabel = orderType === 'Takeaway'
    ? (customerName.trim() ? t('pos.takeawayName', { name: customerName.trim() }) : t('pos.takeaway'))
    : (tableNumber ? t('pos.tableName', { number: tableNumber }) : '');

  return (
    <div className="pos-screen">
      <main className="pos-menu">
        {editing && (
          <div className="pos-editing" role="status">
            <span>{t('pos.editing', { token: editing.tokenNumber })}</span>
            <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={stopEditing}>{t('pos.stopEditing')}</button>
          </div>
        )}
        <div className="pos-type" role="group" aria-label={t('pos.orderType')}>
          {['DineIn', 'Takeaway']
            .filter((type) => !orderSettings || (type === 'DineIn' ? orderSettings.dineInEnabled : orderSettings.takeawayEnabled))
            .map((type) => (
            <button key={type} type="button" className="pos-chip" aria-pressed={orderType === type} disabled={!!editing}
              onClick={() => { typeChosen.current = true; setOrderType(type); }}>
              {type === 'DineIn' ? t('pos.dineIn') : t('pos.takeaway')}
            </button>
          ))}
        </div>
        <div className="pos-filters">
          {orderType === 'DineIn' ? (
            <div className="pos-field-table">
              <label htmlFor="pos-table" className="form-label">{t('pos.table')}</label>
              <select id="pos-table" className="form-select" value={selectedTable} disabled={!!editing} onChange={(e) => setSelectedTable(e.target.value)}>
                <option value="">{t('pos.chooseTable')}</option>
                {tables.map((tb) => (
                  <option key={tb.id} value={tb.id}>{t('pos.tableName', { number: tb.tableNumber })}</option>
                ))}
              </select>
            </div>
          ) : (
            <>
              <div className="pos-field-customer">
                <label htmlFor="pos-customer" className="form-label">{t('pos.customerName')}</label>
                <input id="pos-customer" className="form-control" maxLength={100} autoComplete="off" disabled={!!editing}
                  value={customerName} onChange={(e) => setCustomerName(e.target.value)} />
              </div>
              <div className="pos-field-phone">
                <label htmlFor="pos-phone" className="form-label">{t('pos.customerPhone')}</label>
                <input id="pos-phone" type="tel" inputMode="tel" className="form-control" maxLength={20} autoComplete="off" disabled={!!editing}
                  value={customerPhone} onChange={(e) => setCustomerPhone(e.target.value)} />
              </div>
            </>
          )}
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
                      <span className="pos-item-cat"><VegMark isVeg={item.isVeg} />{item.category}</span>
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
          {orderLabel && <span className="pos-order-table">{orderLabel}</span>}
        </div>
        <div className="pos-order-lines">
          {cart.length === 0 ? (
            <p className="pos-empty-cart">{t('pos.emptyCart')}</p>
          ) : (
            cart.map((l) => (
              <div key={l.id} className="pos-line-wrap">
                <div className="pos-line">
                  <div className="pos-line-info">
                    <div className="pos-line-name">{l.name}</div>
                    <div className="pos-line-price">{formatMoney(l.price * l.quantity)}</div>
                  </div>
                  <button type="button" className="pos-qty-btn" aria-label={t('pos.oneLess', { name: l.name })} onClick={() => changeQty(l, -1)}>−</button>
                  <span className="pos-qty">{l.quantity}</span>
                  <button type="button" className="pos-qty-btn" aria-label={t('pos.oneMore', { name: l.name })} onClick={() => changeQty(l, 1)}>+</button>
                </div>
                {noteOpen === l.id || l.note ? (
                  <input className="form-control form-control-sm pos-note" maxLength={200} aria-label={t('pos.addNote', { name: l.name })}
                    placeholder={t('pos.notePlaceholder')} value={l.note} autoFocus={noteOpen === l.id && !l.note}
                    onChange={(e) => setNote(l.id, e.target.value)} />
                ) : (
                  <button type="button" className="pos-note-btn" aria-label={t('pos.addNote', { name: l.name })} onClick={() => setNoteOpen(l.id)}>
                    + {t('pos.note')}
                  </button>
                )}
              </div>
            ))
          )}
        </div>
        <div className="pos-total">
          <span className="fw-semibold">{t('pos.total', { count: itemCount })}</span>
          <span className="pos-total-amount">{formatMoney(total)}</span>
        </div>
        <button type="button" className="btn btn-primary pos-place" onClick={placeOrder} disabled={placing}>
          {placing ? t('pos.placing') : editing ? t('pos.saveChanges') : t('pos.placeOrder')}
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
