import './App.css';
import POSMenu from './components/POSMenu';
import KitchenDisplay from './components/KitchenDisplay';
import React, { useState } from 'react';

function App() {
  const [currentView, setCurrentView] = useState('pos');

  const handlePOSMenuClick = () => {
    setCurrentView('pos');
  };

  const handleKitchenAdminClick = () => {
    setCurrentView('admin');
  };

  return (
    <div>
      <div style={styles.navBar}>
        <button style={styles.navButton} onClick={handlePOSMenuClick}>
          POS Menu
        </button>
        <button style={styles.navButton} onClick={handleKitchenAdminClick}>
          Kitchen Admin
        </button>
      </div>
      {currentView === 'pos' && <POSMenu />}
      {currentView === 'admin' && <KitchenDisplay />}
    </div>
  );
}

const styles = {
  navBar: {
    display: 'flex',
    justifyContent: 'space-around',
    padding: '10px',
    backgroundColor: '#f8f9fa',
    borderBottom: '1px solid #dee2e6',
  },
  navButton: {
    padding: '10px 20px',
    backgroundColor: '#ffffff',
    border: '1px solid #e0e0e0',
    borderRadius: '4px',
    cursor: 'pointer',
    transition: 'all 0.2s ease-in-out',
    color: '#2c3e50', // Set text color to dark
    fontWeight: 'bold', // Make text bold
  },
};

export default App;
