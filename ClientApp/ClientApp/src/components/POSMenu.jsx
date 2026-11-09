import React, { useState, useEffect } from 'react';

const POSMenu = () => {
    const [menuItems, setMenuItems] = useState([]);
    const [cart, setCart] = useState([]);
    const [activeCategory, setActiveCategory] = useState('All');
    const [selectedTable, setSelectedTable] = useState('');

    useEffect(() => {
        fetch('/api/menuitems')
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok ' + response.statusText);
                }
                return response.json();
            })
            .then(data => {
                setMenuItems(data);
            })
            .catch(error => {
                console.error('Error fetching menu items:', error);
            });
    }, []);

    const addToCart = (item) => {
        setCart((prevCart) => {
            // Check if item already exists in cart to increment quantity
            const existingItem = prevCart.find(i => i.id === item.id);
            if (existingItem) {
                return prevCart.map(i => i.id === item.id ? { ...i, quantity: (i.quantity || 1) + 1 } : i);
            }
            return [...prevCart, { ...item, quantity: 1 }];
        });
    };

    const removeFromCart = (itemId) => {
        setCart(prevCart => prevCart.filter(item => item.id !== itemId));
    };

    const handlePlaceOrder = async () => {
        if (cart.length === 0 || selectedTable === '') {
            alert("Please select a table and add items to the cart.");
            return;
        }

        // Expand cart quantities into a flat array of IDs for the backend
        const itemIds = [];
        cart.forEach(item => {
            for (let i = 0; i < item.quantity; i++) {
                itemIds.push(item.id);
            }
        });

        try {
            const response = await fetch("/api/orders", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ table: parseInt(selectedTable), items: itemIds })
            });

            if (response.ok) {
                const responseData = await response.json(); // Parse response as JSON
                alert("Order placed successfully! Token Number: " + responseData.tokenNumber); // Update success alert
                setCart([]); // Clear the cart
                setSelectedTable(''); // Reset selected table
            } else {
                const errorData = await response.json();
                console.error('Order placement failed:', errorData);
                alert(`Failed to place order: ${errorData.message || 'Unknown error'}`);
            }
        } catch (error) {
            console.error(error);
            alert("Network error.");
        }
    };

    const totalAmount = cart.reduce((sum, item) => sum + (item.price * (item.quantity || 1)), 0);

    const uniqueCategories = ['All', ...new Set(menuItems.map(item => item.category))];

    const handleCategoryFilter = (category) => {
        setActiveCategory(category);
    };

    return (
        <div style={styles.container}>
            {/* Main Menu Section */}
            <div style={styles.mainSection}>
                <header style={styles.header}>
                    <h1 style={styles.title}>POS Menu</h1>
                    <p style={styles.subtitle}>Select items to add to the order</p>
                </header>

                {/* Table Selection */}
                <div style={styles.tableSelection}>
                    <label style={styles.label}>Select Table:</label>
                    <select 
                        value={selectedTable} 
                        onChange={(e) => setSelectedTable(e.target.value)} 
                        style={styles.select}
                    >
                        <option value="">Select a table</option>
                        {Array.from({ length: 10 }, (_, i) => i + 1).map(tableNumber => (
                            <option key={tableNumber} value={tableNumber}>
                                Table {tableNumber}
                            </option>
                        ))}
                    </select>
                </div>

                {/* Category Filter */}
                <div style={styles.categoryFilter}>
                    {uniqueCategories.map(category => (
                        <button 
                            key={category} 
                            style={activeCategory === category ? styles.activeCategoryBtn : styles.filterButton}
                            onClick={() => handleCategoryFilter(category)}
                        >
                            {category}
                        </button>
                    ))}
                </div>

                <div style={styles.grid}>
                    {menuItems
                        .filter(item => activeCategory === 'All' || item.category === activeCategory)
                        .map((item) => (
                            <div 
                                key={item.id} 
                                style={styles.card} 
                                onClick={() => addToCart(item)}
                                onMouseOver={(e) => (e.currentTarget.style.borderColor = '#27ae60')}
                                onMouseOut={(e) => (e.currentTarget.style.borderColor = '#e0e0e0')}
                            >
                                <span style={styles.categoryTag}>{item.category}</span>
                                <h2 style={styles.itemName}>{item.name}</h2>
                                <p style={styles.itemPrice}>₹{item.price.toFixed(2)}</p>
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
                            <div key={item.id} style={styles.cartItem}>
                                <div style={styles.cartItemInfo}>
                                    <span style={styles.cartItemName}>{item.name}</span>
                                    <span style={styles.cartItemQty}>x{item.quantity}</span>
                                </div>
                                <div style={styles.cartItemPrice}>
                                    ₹{(item.price * item.quantity).toFixed(2)}
                                    <button 
                                        onClick={() => removeFromCart(item.id)} 
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
                    <button style={styles.checkoutBtn} onClick={handlePlaceOrder}>Place Order</button>
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
    tableSelection: {
        marginBottom: '20px',
    },
    label: {
        marginRight: '10px',
        fontSize: '1rem',
        color: '#333',
    },
    select: {
        padding: '8px',
        fontSize: '1rem',
        border: '1px solid #e0e0e0',
        borderRadius: '4px',
    },
    categoryFilter: {
        display: 'flex',
        gap: '10px',
        marginBottom: '20px',
    },
    filterButton: {
        padding: '8px 16px',
        backgroundColor: '#ffffff',
        border: '1px solid #e0e0e0',
        borderRadius: '4px',
        cursor: 'pointer',
        transition: 'all 0.2s ease-in-out',
        color: '#333', // Ensure text color is dark enough
    },
    activeCategoryBtn: {
        padding: '8px 16px',
        backgroundColor: '#27ae60',
        color: '#ffffff',
        border: '1px solid #27ae60',
        borderRadius: '4px',
        cursor: 'pointer',
        transition: 'all 0.2s ease-in-out',
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
