import React, { useState, useEffect } from 'react';

const KitchenDisplay = () => {
    const [orders, setOrders] = useState([]);

    // Fetch orders every 5 seconds to keep the kitchen updated
    useEffect(() => {
        const fetchOrders = () => {
            fetch('/api/orders')
                .then(res => res.json())
                .then(data => setOrders(data.filter(o => o.status === "Received")))
                .catch(err => console.error("Error fetching orders:", err));
        };
        
        fetchOrders(); // Initial fetch
        const interval = setInterval(fetchOrders, 5000);
        return () => clearInterval(interval);
    }, []);
	
	const markReady = async (id) => {
    try {
        const response = await fetch(`/api/orders/${id}/status`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ status: 'Ready' }) });
        if (response.ok) {
            // Remove the completed order from the screen instantly
            setOrders(prev => prev.filter(o => o.id !== id));
        }
    } catch (err) {
        console.error("Error marking order as ready:", err);
    }
	};

    return (
        <div style={styles.container}>
            <h2 style={styles.header}>Kitchen Display - Live Orders</h2>
            <div style={styles.grid}>
                {orders.length === 0 ? (
                    <p style={styles.empty}>No pending orders.</p>
                ) : (
                    orders.map(order => (
                        <div key={order.id} style={styles.card}>
                            <div style={styles.cardHeader}>
							<h3 style={styles.orderId}>Token #{order.tokenNumber}</h3>
							<div>
								<span style={styles.tableBadge}>Table {order.table?.tableNumber ?? order.tableId}</span>
								<button 
									onClick={() => markReady(order.id)}
									style={{marginLeft: '15px', padding: '8px 15px', backgroundColor: '#27ae60', color: 'white', border: 'none', borderRadius: '5px', cursor: 'pointer', fontWeight: 'bold'}}>
									Mark Ready
								</button>
							</div>
						</div>
                            <p style={styles.status}>Status: <strong>{order.status}</strong></p>
                            <ul style={styles.itemList}>
                                {order.menuItems && order.menuItems.map((item, index) => (
                                    <li key={index} style={styles.item}>{item.name}</li>
                                ))}
                            </ul>
                        </div>
                    ))
                )}
            </div>
        </div>
    );
};

const styles = {
    container: { padding: '30px', backgroundColor: '#2c3e50', color: 'white', minHeight: '50vh', fontFamily: '"Segoe UI", sans-serif' },
    header: { textAlign: 'center', marginBottom: '30px', color: '#ecf0f1', fontSize: '2.5rem' },
    grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', gap: '25px' },
    card: { backgroundColor: '#ecf0f1', color: '#2c3e50', padding: '20px', borderRadius: '12px', boxShadow: '0 8px 15px rgba(0,0,0,0.2)' },
    cardHeader: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '3px solid #bdc3c7', paddingBottom: '15px', marginBottom: '15px' },
    orderId: { margin: 0, fontSize: '1.5rem', color: '#34495e' },
    tableBadge: { backgroundColor: '#e74c3c', color: 'white', padding: '8px 15px', borderRadius: '25px', fontWeight: 'bold', fontSize: '1.2rem' },
    status: { fontSize: '1.2rem', color: '#d35400', margin: '0 0 15px 0' },
    itemList: { paddingLeft: '25px', margin: 0 },
    item: { fontSize: '1.3rem', paddingBottom: '8px', fontWeight: '500' },
    empty: { fontSize: '1.5rem', gridColumn: '1 / -1', textAlign: 'center', color: '#95a5a6' }
};

export default KitchenDisplay;
