import React, { useState, useEffect } from 'react';

function Menu({ table }) {
  const [menuItems, setMenuItems] = useState([]);
  const [cart, setCart] = useState([]);

  useEffect(() => {
    fetchMenuItems();
  }, []);

  const fetchMenuItems = async () => {
    try {
      const response = await fetch('https://localhost:7001/api/menuitems');
      const data = await response.json();
      setMenuItems(data);
    } catch (error) {
      console.error('Error fetching menu items:', error);
    }
  };

  const addToCart = (item) => {
    setCart([...cart, item]);
  };

  const removeFromCart = (index) => {
    const newCart = [...cart];
    newCart.splice(index, 1);
    setCart(newCart);
  };

  const submitOrder = async () => {
    if (cart.length === 0) return;

    const order = {
      tableId: table.id,
      menuItems: cart,
      status: "Pending"
    };

    try {
      const response = await fetch('https://localhost:7001/api/orders', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(order),
      });

      if (response.ok) {
        alert('Order submitted successfully!');
        setCart([]);
      } else {
        alert('Error submitting order');
      }
    } catch (error) {
      console.error('Error submitting order:', error);
    }
  };

  const categories = [...new Set(menuItems.map(item => item.category))];

  return (
    <div className="menu">
      <h2>Menu for Table {table.tableNumber}</h2>
      <div className="menu-categories">
        {categories.map(category => (
          <div key={category} className="category-section">
            <h3>{category}</h3>
            <div className="menu-items">
              {menuItems.filter(item => item.category === category).map(item => (
                <div key={item.id} className="menu-item">
                  <span>{item.name}</span>
                  <span>₹{item.price.toFixed(2)}</span>
                  <button onClick={() => addToCart(item)}>Add</button>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
      <div className="cart">
        <h3>Cart</h3>
        {cart.map((item, index) => (
          <div key={index} className="cart-item">
            <span>{item.name}</span>
            <span>₹{item.price.toFixed(2)}</span>
            <button onClick={() => removeFromCart(index)}>Remove</button>
          </div>
        ))}
        <div className="cart-total">
          <strong>Total: ₹{cart.reduce((sum, item) => sum + item.price, 0).toFixed(2)}</strong>
        </div>
        <button onClick={submitOrder} disabled={cart.length === 0}>
          Submit Order
        </button>
      </div>
    </div>
  );
}

export default Menu;
