import React from 'react';
import { printT, billDateTime } from './printText';
import './print.css';

// Kitchen order ticket: what to cook, big and plain; no prices.
export default function KotTicket({ order, lang = 'en', width = 80 }) {
  const t = printT(lang);
  const dineIn = order.orderType === 'DineIn';
  const items = order.items ?? [];
  return (
    <div className={`receipt receipt-${width}`} data-testid="kot-ticket">
      <div className="receipt-center">
        <div className="receipt-title">{t('print.kot')}</div>
        <div className="kot-token">#{order.tokenNumber}</div>
        <div className="receipt-big">
          {dineIn && order.table ? t('print.table', { table: order.table.tableNumber }) : t('print.takeaway')}
        </div>
        {!dineIn && order.customerName && <div>{order.customerName}</div>}
        <div>{billDateTime(order.createdAt)}</div>
      </div>
      <div className="receipt-line" />
      {items.map((item) => (
        <div key={item.id}>
          <div className="receipt-row kot-item"><span>{item.itemName}</span><span>x {item.quantity}</span></div>
          {item.options && <div className="kot-note">+ {item.options}</div>}
          {item.note && <div className="kot-note">{t('print.note')}: {item.note}</div>}
        </div>
      ))}
      <div className="receipt-line" />
      <div className="receipt-center">{t('print.items', { count: items.reduce((sum, i) => sum + i.quantity, 0) })}</div>
    </div>
  );
}
