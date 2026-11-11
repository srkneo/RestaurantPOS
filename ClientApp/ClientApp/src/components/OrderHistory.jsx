import React, { useState, useEffect } from 'react';

const OrderHistory = () => {
  const [orders, setOrders] = useState([]);

  useEffect(() => {
    fetch('/api/orders')
      .then(response => response.json())
      .then(data => setOrders(data))
      .catch(error => console.error('Error fetching orders:', error));
  }, []);

  // Works with both item shapes: items (itemName, unitPrice) and menuItems (name, price)
  const getItems = (order) =>
    (order.items ?? order.menuItems ?? []).map(i => ({
      name: i.itemName ?? i.name,
      quantity: i.quantity ?? 1,
      price: Number(i.unitPrice ?? i.price ?? 0),
    }));

  return (
    <div style={{ padding: '30px' }}>
      <h1>Order History</h1>
      {orders.length === 0 ? (
        <p>No orders yet.</p>
      ) : (
        <ul style={{ listStyle: 'none', padding: 0 }}>
          {[...orders].reverse().map(order => {
            const items = getItems(order);
            const total = items.reduce((sum, i) => sum + i.price * i.quantity, 0);
            return (
              <li key={order.id} style={{ border: '1px solid #ddd', borderRadius: '8px', padding: '15px', marginBottom: '12px' }}>
                <strong>Token #{order.tokenNumber}</strong> | Table {order.table?.tableNumber ?? order.tableId} | {order.status}
                <br />
                <small>{new Date(order.createdAt).toLocaleString()}</small>
                <br />
                {items.map(i => `${i.quantity} x ${i.name}`).join(', ')}
                <br />
                <strong>Total:</strong> {'\u20B9'}{total.toFixed(2)}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
};

export default OrderHistory;