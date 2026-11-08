import React, { useState, useEffect } from 'react';

const OrderHistory = () => {
  const [orders, setOrders] = useState([]);

  useEffect(() => {
    fetch('http://localhost:5197/api/orders')
      .then(response => response.json())
      .then(data => {
        setOrders(data);
      })
      .catch(error => {
        console.error('Error fetching orders:', error);
      });
  }, []);

  return (
    <div>
      <h1>Order History</h1>
      <ul>
        {orders.map(order => (
          <li key={order.id}>
            <strong>Order ID:</strong> {order.id}<br />
            <strong>Table:</strong> {order.tableNumber}<br />
            <strong>Items:</strong> {order.items.map(item => `${item.name} x${item.quantity}`).join(', ')}<br />
            <strong>Total:</strong> ${order.total.toFixed(2)}
          </li>
        ))}
      </ul>
    </div>
  );
};

export default OrderHistory;
