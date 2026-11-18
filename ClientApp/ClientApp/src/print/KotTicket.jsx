import React from 'react';
import { printT, billDateTime } from './printText';
import './print.css';

// Lines grouped by kitchen station: the main kitchen (no station) first, then stations in the order they appear.
function byStation(items) {
  const groups = [];
  for (const item of items) {
    const key = item.stationName || '';
    let group = groups.find((g) => g.key === key);
    if (!group) {
      group = { key, items: [] };
      groups.push(group);
    }
    group.items.push(item);
  }
  groups.sort((a, b) => (a.key === '' ? -1 : 0) - (b.key === '' ? -1 : 0));
  return groups.length > 0 ? groups : [{ key: '', items: [] }];
}

// Kitchen order ticket: what to cook, big and plain; no prices.
// With kitchen stations, one ticket per station is printed, each on its own page (the printer cuts between them).
export default function KotTicket({ order, lang = 'en', width = 80, round = null }) {
  const t = printT(lang);
  const dineIn = order.orderType === 'DineIn';
  // A running tab prints only the round just added (/print/kot/12?round=2).
  const all = order.items ?? [];
  const groups = byStation(round ? all.filter((i) => (i.round ?? 1) === round) : all);
  const showStation = groups.length > 1 || groups[0].key !== '';
  return (
    <>
      {groups.map((group, index) => (
        <div key={group.key || 'main'} className={`receipt receipt-${width}${index > 0 ? ' kot-break' : ''}`} data-testid="kot-ticket">
          <div className="receipt-center">
            <div className="receipt-title">{t('print.kot')}</div>
            {showStation && <div className="receipt-big kot-station">{group.key || t('print.mainKitchen')}</div>}
            {round > 1 && <div className="receipt-big">{t('print.round', { round })}</div>}
            <div className="kot-token">#{order.tokenNumber}</div>
            <div className="receipt-big">
              {dineIn && order.table ? t('print.table', { table: order.table.tableNumber }) : t('print.takeaway')}
            </div>
            {!dineIn && order.customerName && <div>{order.customerName}</div>}
            <div>{billDateTime(order.createdAt)}</div>
          </div>
          <div className="receipt-line" />
          {group.items.map((item) => (
            <div key={item.id}>
              <div className="receipt-row kot-item"><span>{item.itemName}</span><span>x {item.quantity}</span></div>
              {item.options && <div className="kot-note">+ {item.options}</div>}
              {item.includes && <div className="kot-note">= {item.includes}</div>}
              {item.note && <div className="kot-note">{t('print.note')}: {item.note}</div>}
            </div>
          ))}
          <div className="receipt-line" />
          <div className="receipt-center">{t('print.items', { count: group.items.reduce((sum, i) => sum + i.quantity, 0) })}</div>
        </div>
      ))}
    </>
  );
}
