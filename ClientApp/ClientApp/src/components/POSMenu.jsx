import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import * as signalR from '@microsoft/signalr';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import ConfirmDialog from '../ui/ConfirmDialog';
import BillDialog from './BillDialog';
import ItemOptionsDialog from './ItemOptionsDialog';
import StateMessage from '../ui/StateMessage';
import { formatMoney, minutesSince, newClientId } from '../ui/format';
import './POSMenu.css';
import { useAuth } from '../AuthContext';
import { saveOffline, isUnreachable } from '../offline/orderQueue';
import { readMenuCache, writeMenuCache } from '../offline/menuCache';

const MAX_QTY = 99;

// One order line per item, size and set of extras.
const lineKey = (id, variantId, addOnIds) => `${id}|${variantId ?? ''}|${[...(addOnIds ?? [])].sort((a, b) => a - b).join(',')}`;
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
  const [offlineSince, setOfflineSince] = useState(null); // set when the menu comes from this till's saved copy
  const auth = useAuth();
  // Captain mode: a waiter (and no till role) takes orders but does not bill.
  const userRoles = auth?.user?.roles ?? [];
  const isWaiter = userRoles.includes('waiter') && !['cashier', 'manager', 'admin'].some((r) => userRoles.includes(r));
  const cacheKey = `pos-menu:${auth?.user?.email ?? 'anon'}`;
  const [cart, setCart] = useState([]);
  const [category, setCategory] = useState(ALL);
  const [search, setSearch] = useState('');
  const [orderType, setOrderType] = useState('DineIn');
  const [selectedTable, setSelectedTable] = useState('');
  const [tableGrid, setTableGrid] = useState(false);
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [noteOpen, setNoteOpen] = useState(null);
  // The item whose size and extras are being chosen.
  const [choosing, setChoosing] = useState(null);
  const [placing, setPlacing] = useState(false);
  const [confirmClear, setConfirmClear] = useState(false);
  const [editing, setEditing] = useState(null); // { id, tokenNumber, version }
  const [lastPlaced, setLastPlaced] = useState(null); // { id, tokenNumber } of the order just placed
  const [billOrderId, setBillOrderId] = useState(null);
  const [orderSettings, setOrderSettings] = useState(null);
  const clientOrderId = useRef(newClientId());
  const tRef = useRef(t);
  // The cashier's own choice wins over the outlet default that arrives with the settings.
  const typeChosen = useRef(false);

  // Table status (free, occupied, billed) changes as orders come and go; only the tables are loaded again for it.
  const refreshTables = useCallback(async () => {
    try {
      const res = await fetch('/api/tables');
      if (res.ok) setTables(await res.json());
    } catch {
      // Keeps the last list; the next change tries again.
    }
  }, []);

  useEffect(() => {
    tRef.current = t;
  }, [t]);

  // silent: try again in the background without replacing the screen by "Loading".
  const load = useCallback(async (silent) => {
    if (silent !== true) setLoadState('loading');
    // Outlet settings decide which order types the POS offers and which one it starts with.
    const applySettings = (settings) => {
      setOrderSettings(settings);
      if (!editId && !typeChosen.current && settings.defaultOrderType) setOrderType(settings.defaultOrderType);
    };
    try {
      const [menuRes, tablesRes, settingsRes] = await Promise.all([fetch('/api/menuitems'), fetch('/api/tables'), fetch('/api/store/settings')]);
      if (!menuRes.ok || !tablesRes.ok) throw new Error('load failed');
      const menu = await menuRes.json();
      const tableList = await tablesRes.json();
      const settings = settingsRes?.ok ? await settingsRes.json() : null;
      setMenuItems(menu);
      setTables(tableList);
      if (settings) applySettings(settings);
      setOfflineSince(null);
      writeMenuCache(cacheKey, { menu, tables: tableList, settings });
      setLoadState('ready');
    } catch {
      // No server: work from the menu saved the last time this till was online.
      const cached = readMenuCache(cacheKey);
      if (cached?.menu && cached?.tables) {
        setMenuItems(cached.menu);
        setTables(cached.tables);
        if (cached.settings) applySettings(cached.settings);
        setOfflineSince(cached.savedAt);
        setLoadState('ready');
      } else {
        setLoadState('error');
      }
    }
  }, [editId, cacheKey]);

  useEffect(() => {
    load();
  }, [load]);

  // While working from the saved menu, quietly try the server again: when the browser is back online and every 30 seconds.
  useEffect(() => {
    if (!offlineSince) return undefined;
    const retry = () => load(true);
    window.addEventListener('online', retry);
    const timer = setInterval(retry, 30000);
    return () => {
      window.removeEventListener('online', retry);
      clearInterval(timer);
    };
  }, [offlineSince, load]);

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
        setCart(order.items.map((i) => ({
          key: lineKey(i.menuItemId, i.variantId, i.addOnIds), id: i.menuItemId, name: i.itemName, price: Number(i.unitPrice),
          quantity: i.quantity, note: i.note || '', variantId: i.variantId ?? null, addOnIds: i.addOnIds ?? [], options: i.options ?? null,
        })));
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
      refreshTables();
    });
    connection.on('ReceiveNewOrder', () => refreshTables());
    connection.on('OrderUpdated', () => refreshTables());
    // A guest at a table pressed Call waiter or Ask for bill on the QR page.
    connection.on('TableCall', (call) => {
      showToast(tRef.current(call.kind === 'bill' ? 'qr.tableWantsBill' : 'qr.tableCalls', { number: call.tableNumber }));
    });
    // A guest gave 1 or 2 stars from the feedback QR on the bill.
    connection.on('LowRating', (r) => {
      showToast(tRef.current('feedback.lowRating', { rating: r.rating, label: r.orderLabel ?? '', number: r.invoiceNumber }));
    });
    const started = connection.start().catch((err) => console.error(err));
    return () => {
      started.then(() => connection.stop());
    };
  }, [showToast, refreshTables]);

  // Adds one of a line (item, size and extras). The same item with another size or other extras is its own line.
  const addLine = (line) => {
    const key = lineKey(line.id, line.variantId, line.addOnIds);
    setCart((prev) => {
      const found = prev.find((l) => l.key === key);
      if (!found) return [...prev, { variantId: null, addOnIds: [], options: null, ...line, key, quantity: 1, note: '' }];
      if (found.quantity >= MAX_QTY) return prev;
      return prev.map((l) => (l.key === key ? { ...l, quantity: l.quantity + 1 } : l));
    });
  };

  // Tapping an item: straight into the order, or first the size and extras when it has any.
  const tapItem = (item) => {
    if ((item.variants?.length ?? 0) > 0 || (item.addOns?.length ?? 0) > 0) {
      setChoosing(item);
      return;
    }
    addLine({ id: item.id, name: item.name, price: Number(item.price) });
  };

  const changeQty = (line, delta) => {
    setCart((prev) => {
      const quantity = line.quantity + delta;
      if (quantity <= 0) return prev.filter((l) => l.key !== line.key);
      if (quantity > MAX_QTY) return prev;
      return prev.map((l) => (l.key === line.key ? { ...l, quantity } : l));
    });
  };

  const setNote = (key, note) => setCart((prev) => prev.map((l) => (l.key === key ? { ...l, note } : l)));

  const lines = () => cart.map((l) => ({
    menuItemId: l.id, quantity: l.quantity, note: l.note?.trim() || null,
    ...(l.variantId ? { variantId: l.variantId } : {}),
    ...(l.addOnIds?.length ? { addOnIds: l.addOnIds } : {}),
  }));

  const tableInfo = orderType === 'DineIn' ? tables.find((tb) => String(tb.id) === selectedTable) : null;
  // Running tab: the table already has an open order, so these items become its next round.
  const tab = !editing && tableInfo?.status === 'Occupied' && tableInfo.orderId ? tableInfo : null;

  const addRound = async (open) => {
    try {
      const res = await fetch(`/api/orders/${open.orderId}/add-items`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ items: lines(), version: open.orderVersion }),
      });
      const data = await res.json().catch(() => ({}));
      if (res.status === 409) {
        showToast(data.code === 'billed' ? t('pos.tabBilled', { number: open.tableNumber }) : t('pos.conflict'), 'error');
        refreshTables();
        return;
      }
      if (!res.ok) {
        showToast(data.message || t('pos.orderFailed'), 'error');
        return;
      }
      const round = Math.max(1, ...(data.items ?? []).map((i) => i.round ?? 1));
      showToast(t('pos.roundAdded', { token: open.tokenNumber }));
      setLastPlaced({ id: open.orderId, tokenNumber: open.tokenNumber, round });
      resetOrder();
      refreshTables();
    } catch {
      showToast(t('pos.networkError'), 'error');
    }
  };

  const placeOrder = async () => {
    if (!editing && tableInfo?.status === 'Billed') {
      showToast(t('pos.tabBilled', { number: tableInfo.tableNumber }), 'error');
      return;
    }
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
    const newOrder = editing ? null : {
      orderType,
      table: orderType === 'DineIn' ? Number(selectedTable) : null,
      customerName: customerName.trim() || null,
      customerPhone: customerPhone.trim() || null,
      items: lines(),
      clientOrderId: clientOrderId.current,
    };
    // No server: keep the new order on this till; it is sent by itself when the connection is back (same clientOrderId, so never twice).
    const keepOffline = async () => {
      await saveOffline(newOrder);
      showToast(t('pos.savedOffline'));
      resetOrder();
    };
    setPlacing(true);
    if (tab) {
      await addRound(tab);
      setPlacing(false);
      return;
    }
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
          body: JSON.stringify(newOrder),
        });
      if (!editing && isUnreachable(res.status)) {
        await keepOffline();
        return;
      }
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
        setLastPlaced({ id: data.orderId, tokenNumber: data.tokenNumber });
        refreshTables();
        resetOrder();
      }
    } catch {
      if (!editing) {
        await keepOffline();
        return;
      }
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

  const qtyOf = (id) => cart.filter((l) => l.id === id).reduce((sum, l) => sum + l.quantity, 0);
  const itemCount = cart.reduce((sum, l) => sum + l.quantity, 0);
  const orderPanel = useRef(null);
  const total = cart.reduce((sum, l) => sum + l.price * l.quantity, 0);
  const tableNumber = tables.find((tb) => String(tb.id) === selectedTable)?.tableNumber;
  const orderLabel = orderType === 'Takeaway'
    ? (customerName.trim() ? t('pos.takeawayName', { name: customerName.trim() }) : t('pos.takeaway'))
    : (tableNumber ? t('pos.tableName', { number: tableNumber }) : '');

  return (
    <div className={`pos-screen${cart.length > 0 ? ' has-cart-bar' : ''}`}>
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
              {/* One picker: shows the chosen table and opens the grid with every table's state. */}
              <button id="pos-table" type="button" className="form-select text-start" disabled={!!editing} onClick={() => setTableGrid(true)}>
                {!tableInfo ? t('pos.chooseTable')
                  : tableInfo.status === 'Occupied' ? t('pos.tableBusy', { number: tableInfo.tableNumber, token: tableInfo.tokenNumber })
                    : tableInfo.status === 'Billed' ? t('pos.tableBilled', { number: tableInfo.tableNumber })
                      : t('pos.tableName', { number: tableInfo.tableNumber })}
              </button>
              {tab && <div className="pos-hint pos-tab-hint" role="status">{t('pos.tabOpen', { number: tab.tableNumber, token: tab.tokenNumber })}</div>}
              {!editing && tableInfo?.status === 'Billed' && (
                <div className="pos-hint text-danger" role="status">{t('pos.tabBilled', { number: tableInfo.tableNumber })}</div>
              )}
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
        {loadState === 'ready' && offlineSince && (
          <p className="alert alert-warning py-2 mb-2" role="status">
            {t('pos.offlineMenu', { time: new Date(offlineSince).toLocaleString('en-IN', { timeZone: 'Asia/Kolkata', dateStyle: 'short', timeStyle: 'short' }) })}
          </p>
        )}
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
                      aria-label={t('pos.addItem', { name: item.name })} onClick={() => tapItem(item)}>
                      <span className="pos-item-cat"><VegMark isVeg={item.isVeg} />{item.category}</span>
                      <span className="pos-item-name">{item.name}</span>
                      {(item.includes || item.description) && (
                        <span className="pos-item-desc">{item.includes ? item.includes.split(', ').join(' · ') : item.description}</span>
                      )}
                      <span className="pos-item-foot">
                        {item.variants?.length
                          ? t('pos.fromPrice', { price: formatMoney(Math.min(...item.variants.map((v) => Number(v.price)))) })
                          : formatMoney(item.price)}
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

      <aside className="pos-order" aria-label={t('pos.currentOrder')} ref={orderPanel}>
        <div className="pos-order-head">
          <h2>{t('pos.currentOrder')}</h2>
          {orderLabel && <span className="pos-order-table">{orderLabel}</span>}
        </div>
        <div className="pos-order-lines">
          {cart.length === 0 ? (
            <p className="pos-empty-cart">{t('pos.emptyCart')}</p>
          ) : (
            cart.map((l) => (
              <div key={l.key} className="pos-line-wrap">
                <div className="pos-line">
                  <div className="pos-line-info">
                    <div className="pos-line-name">{l.name}</div>
                    {l.options && <div className="pos-line-options">{l.options}</div>}
                    <div className="pos-line-price">{formatMoney(l.price * l.quantity)}</div>
                  </div>
                  <button type="button" className="pos-qty-btn" aria-label={t('pos.oneLess', { name: l.name })} onClick={() => changeQty(l, -1)}>−</button>
                  <span className="pos-qty">{l.quantity}</span>
                  <button type="button" className="pos-qty-btn" aria-label={t('pos.oneMore', { name: l.name })} onClick={() => changeQty(l, 1)}>+</button>
                </div>
                {noteOpen === l.key || l.note ? (
                  <input className="form-control form-control-sm pos-note" maxLength={200} aria-label={t('pos.addNote', { name: l.name })}
                    placeholder={t('pos.notePlaceholder')} value={l.note} autoFocus={noteOpen === l.key && !l.note}
                    onChange={(e) => setNote(l.key, e.target.value)} />
                ) : (
                  <button type="button" className="pos-note-btn" aria-label={t('pos.addNote', { name: l.name })} onClick={() => setNoteOpen(l.key)}>
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
        {lastPlaced && (
          <div className="pos-last-order" role="status">
            <span className="fw-semibold">{t('bill.lastOrder', { token: lastPlaced.tokenNumber })}</span>
            <div className="d-flex gap-2">
              {!isWaiter && (
                <button type="button" className="btn btn-outline-primary btn-sm px-3" onClick={() => setBillOrderId(lastPlaced.id)}>{t('bill.billAndPay')}</button>
              )}
              <button type="button" className="btn btn-outline-secondary btn-sm px-3"
                onClick={() => window.open(`/print/kot/${lastPlaced.id}?auto=1${lastPlaced.round > 1 ? `&round=${lastPlaced.round}` : ''}`, '_blank', 'noopener')}>
                {t('bill.printKot')}
              </button>
            </div>
          </div>
        )}
        <button type="button" className="btn btn-primary pos-place" onClick={placeOrder} disabled={placing}>
          {placing ? t('pos.placing') : editing ? t('pos.saveChanges') : tab ? t('pos.addToOrder', { token: tab.tokenNumber }) : t('pos.placeOrder')}
        </button>
        <button type="button" className="btn btn-outline-secondary" onClick={() => setConfirmClear(true)} disabled={cart.length === 0}>
          {t('pos.clearOrder')}
        </button>
      </aside>

      {/* Phones and tablets: the order is below a long menu, so this bar shows the total and jumps to it. */}
      {cart.length > 0 && (
        <div className="pos-cart-bar d-lg-none" role="region" aria-label={t('pos.orderBar')}>
          <strong className="pos-cart-bar-text">{t('pos.barSummary', { count: itemCount, total: formatMoney(total) })}</strong>
          <button type="button" className="btn btn-primary px-4"
            onClick={() => orderPanel.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })}>
            {t('pos.reviewOrder')}
          </button>
        </div>
      )}

      <ConfirmDialog open={confirmClear} title={t('pos.clearTitle')} confirmLabel={t('pos.clearConfirm')} danger
        onCancel={() => setConfirmClear(false)}
        onConfirm={() => { setCart([]); setConfirmClear(false); }} />
      <ItemOptionsDialog item={choosing} onCancel={() => setChoosing(null)}
        onAdd={(line) => { addLine(line); setChoosing(null); }} />
      <BillDialog orderId={billOrderId} onClose={() => setBillOrderId(null)} />
      {tableGrid && (
        <div className="pos-table-grid-backdrop" role="presentation" onClick={() => setTableGrid(false)}>
          <div className="pos-table-grid" role="dialog" aria-modal="true" aria-label={t('pos.tableGrid')} onClick={(e) => e.stopPropagation()}>
            <div className="d-flex justify-content-between align-items-center mb-2">
              <h2 className="h5 mb-0">{t('pos.tableGrid')}</h2>
              <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => setTableGrid(false)}>{t('common.close')}</button>
            </div>
            <div className="pos-table-tiles">
              {tables.map((tb) => {
                const status = tb.status ?? 'Free';
                const minutes = tb.since ? minutesSince(tb.since) : null;
                return (
                  <button key={tb.id} type="button" className={`pos-table-tile pos-tile-${status.toLowerCase()}`}
                    aria-pressed={String(tb.id) === selectedTable} aria-label={t('pos.tableName', { number: tb.tableNumber })}
                    onClick={() => { setSelectedTable(String(tb.id)); setTableGrid(false); }}>
                    <span className="pos-tile-number">{tb.tableNumber}</span>
                    <span className="pos-tile-status">
                      {status === 'Occupied' ? t('pos.tileBusy', { token: tb.tokenNumber, minutes: minutes ?? 0 })
                        : status === 'Billed' ? t('pos.tileBilled') : t('pos.tileFree')}
                    </span>
                  </button>
                );
              })}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default POSMenu;
