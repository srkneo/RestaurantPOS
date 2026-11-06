import React, { useState, useEffect } from 'react';
import Menu from './Menu';

function App() {
  const [tables, setTables] = useState([]);
  const [selectedTable, setSelectedTable] = useState(null);

  useEffect(() => {
    fetchTables();
  }, []);

  const fetchTables = async () => {
    try {
      const response = await fetch('https://localhost:7001/api/tables');
      const data = await response.json();
      setTables(data);
    } catch (error) {
      console.error('Error fetching tables:', error);
    }
  };

  return (
    <div className="App">
      <h1>Restaurant POS</h1>
      <div className="table-selection">
        <h2>Select a Table</h2>
        {tables.map(table => (
          <button 
            key={table.id} 
            onClick={() => setSelectedTable(table)}
            className={selectedTable?.id === table.id ? 'selected' : ''}
          >
            Table {table.tableNumber}
          </button>
        ))}
      </div>
      {selectedTable && <Menu table={selectedTable} />}
    </div>
  );
}

export default App;
