import React, { useState, useEffect } from 'react';

const POSMenu = () => {
    const [menuItems, setMenuItems] = useState([]);
    const [cart, setCart] = useState([]);

    useEffect(() => {
        fetch('http://localhost:5197/api/menuitems')
            .then(response => response.json())
            .then(data => {
                setMenuItems(data);
            })
            .catch(error => {
                console.error('Error fetching menu items:', error);
            });
    }, []);

    const addToCart = (item) => {
        setCart((prevCart) => {
            // Check if item already exists in cart to increment quantity (optional logic)
            const existingItem = prevCart.find(i => i.Id === item.Id);
            if (existingItem) {
                return prevCart.map(i => i.Id === item.Id ? { ...i, quantity: (i.quantity || 1) + 1 } : i);
            }
            return [...prevCart, { ...item, quantity: 1 }];
        });
    };

    const removeFromCart = (itemId) => {
        setCart(prevCart => prevCart.filter(item => item.Id !== itemId));
    };

    const totalAmount = cart.reduce((sum, item) => sum + (item.Price * (item.quantity || 1)), 0);

    return (
        <div style={styles.container}>
            {/* Main Menu Section */}
            <div style={styles.mainSection}>
                <header style={styles.header}>
                    <h1 style={styles.title}>POS Menu</h1>
                    <p style={styles.subtitle}>Select items to add to the order</p>
                </header>

                <div style={styles.grid}>
                    {menuItems.map((item) => (
                        <div 
                            key={item.Id} 
                            style={styles.card} 
                            onClick={() => addToCart(item)}
                            onMouseOver={(e) => (e.currentTarget.style.borderColor = '#27ae60')}
                            onMouseOut={(e) => (e.currentTarget.style.borderColor = '#e0e0e0')}
                        >
                            <span style={styles.categoryTag}>{item.Category}</span>
                            <h2 style={styles.itemName}>{item.Name}</h2>
                            <p style={styles.itemPrice}>₹{item.Price.toFixed(2)}</p>
                        </div>
                    ))}
                </div>
            </div>

            {/* Order Summary Sidebar */}
            <div style={styles.sidebar}>
                <h2 style={styles.sidebarTitle}>Current Order</h2>
                <div style={styles.cartList}>
                    {cart.length === 0 ? (
                        <p style={styles.emptyMsg}>Your cart is empty</p>
                    ) : (
                        cart.map((item) => (
                            <div key={item.Id} style={styles.cartItem}>
                                <div style={styles.cartItemInfo}>
                                    <span style={styles.cartItemName}>{item.Name}</span>
                                    <span style={styles.cartItemQty}>x{item.quantity}</span>
                                </div>
                                <div style={styles.cartItemPrice}>
                                    ₹{(item.Price * item.quantity).toFixed(2)}
                                    <button 
                                        onClick={() => removeFromCart(item.Id)} 
                                        style={styles.removeBtn}
                                    >
                                        ✕
                                    </button>
                                </div>
                            </div>
                        ))
                    )}
                </div>
                <div style={styles.footer}>
                    <div style={styles.totalRow}>
                        <span>Total:</span>
                        <span>₹{totalAmount.toFixed(2)}</span>
                    </div>
                    <button style={styles.checkoutBtn}>Place Order</button>
                </div>
            </div>
        </div>
    );
};

const styles = {
    container: {
        display: 'flex',
        height: '100vh',
        backgroundColor: '#f8f9fa',
        fontFamily: '"Segoe UI", Roboto, Helvetica, Arial, sans-serif',
    },
    mainSection: {
        flex: 1,
        padding: '40px',
        overflowY: 'auto',
    },
    header: {
        marginBottom: '30px',
    },
    title: {
        margin: 0,
        fontSize: '2.5rem',
        color: '#2c3e50',
    },
    subtitle: {
        color: '#7f8c8d',
        fontSize: '1.1rem',
    },
    grid: {
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
        gap: '25px',
    },
    card: {
        backgroundColor: '#ffffff',
        padding: '20px',
        borderRadius: '12px',
        border: '2px solid #e0e0e0',
        boxShadow: '0 4px 6px rgba(0,0,0,0.05)',
        cursor: 'pointer',
        transition: 'all 0.2s ease-in-out',
        textAlign: 'center',
    },
    categoryTag: {
        fontSize: '0.75rem',
        backgroundColor: '#ecf0f1',
        color: '#7f8c8d',
        padding: '4px 12px',
        borderRadius: '20px',
        textTransform: 'uppercase',
        fontWeight: 'bold',
    },
    itemName: {
        margin: '15px 0 10px 0',
        fontSize: '1.4rem',
        color: '#34495e',
    },
    itemPrice: {
        fontSize: '1.2rem',
        fontWeight: 'bold',
        color: '#27ae60',
    },
    sidebar: {
        width: '380px',
        backgroundColor: '#ffffff',
        borderLeft: '1px solid #dee2e6',
        padding: '30px',
        display: 'flex',
        flexDirection: 'column',
        boxShadow: '-5px 0 15px rgba(0,0,0,0.03)',
    },
    sidebarTitle: {
        margin: '0 0 20px 0',
        fontSize: '1.5rem',
        color: '#2c3e50',
    },
    cartList: {
        flex: 1,
        overflowY: 'auto',
        marginBottom: '20px',
    },
    emptyMsg: {
        color: '#bdc3c7',
        fontStyle: 'italic',
        textAlign: 'center',
        marginTop: '50px',
    },
    cartItem: {
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        padding: '12px 0',
        borderBottom: '1px solid #f1f1f1',
    },
    cartItemInfo: {
        display: 'flex',
        flexDirection: 'column',
    },
    cartItemName: {
        fontWeight: '600',
        color: '#2c3e50',
    },
    cartItemQty: {
        fontSize: '0.85rem',
        color: '#7f8c8d',
    },
    cartItemPrice: {
        display: 'flex',
        alignItems: 'center',
        gap: '10px',
    },
    removeBtn: {
        background: 'none',
        border: 'none',
        color: '#e74c3c',
        cursor: 'pointer',
        fontSize: '1.1rem',
    },
    footer: {
        borderTop: '2px solid #eee',
        paddingTop: '20px',
    },
    totalRow: {
        display: 'flex',
        justifyContent: 'space-between',
        fontSize: '1.5rem',
        fontWeight: 'bold',
        marginBottom: '20px',
        color: '#2c3e50',
    },
    checkoutBtn: {
        width: '100%',
        padding: '15px',
        backgroundColor: '#27ae60',
        color: 'white',
        border: 'none',
        borderRadius: '8px',
        fontSize: '1.2rem',
        fontWeight: 'bold',
        cursor: 'pointer',
        transition: 'background 0.2s',
    },
};

export default POSMenu;
