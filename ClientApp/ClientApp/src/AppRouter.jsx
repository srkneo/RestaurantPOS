import './App.css';
import React from 'react';
import { BrowserRouter, Routes, Route, NavLink, Navigate, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './AuthContext';
import RequireRole from './components/RequireRole';
import POSMenu from './components/POSMenu';
import KitchenDisplay from './components/KitchenDisplay';
import OrderHistory from './components/OrderHistory';
import Login from './components/Login';

function HomeRedirect() {
  const { user, loading } = useAuth();
  if (loading) return null;
  if (!user) return <Navigate to="/login" replace />;
  const kitchenOnly = user.roles.length === 1 && user.roles[0] === 'kitchen';
  return <Navigate to={kitchenOnly ? '/kitchen' : '/pos'} replace />;
}

function NavBar() {
  const { user, logout } = useAuth();
  const can = (roles) => user && roles.some((r) => user.roles.includes(r));
  return (
    <div style={styles.navBar}>
      {can(['cashier', 'manager', 'admin']) && <NavLink to="/pos" style={styles.navButton}>POS Menu</NavLink>}
      {can(['cashier', 'manager', 'admin']) && <NavLink to="/orders" style={styles.navButton}>Order History</NavLink>}
      {can(['kitchen', 'manager', 'admin']) && <NavLink to="/kitchen" style={styles.navButton}>Kitchen</NavLink>}
      {user && <button style={styles.navButton} onClick={logout}>Sign out ({user.email})</button>}
    </div>
  );
}

function Layout() {
  const { pathname } = useLocation();
  const hideNav = pathname.startsWith('/kitchen') || pathname.startsWith('/login');
  return (
    <div>
      {!hideNav && <NavBar />}
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/" element={<HomeRedirect />} />
        <Route path="/pos" element={<RequireRole roles={['cashier', 'manager', 'admin']}><POSMenu /></RequireRole>} />
        <Route path="/orders" element={<RequireRole roles={['cashier', 'manager', 'admin']}><OrderHistory /></RequireRole>} />
        <Route path="/kitchen" element={<RequireRole roles={['kitchen', 'manager', 'admin']}><KitchenDisplay /></RequireRole>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </div>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Layout />
      </AuthProvider>
    </BrowserRouter>
  );
}

const styles = {
  navBar: { display: 'flex', justifyContent: 'space-around', padding: '10px', backgroundColor: '#f8f9fa', borderBottom: '1px solid #dee2e6' },
  navButton: { padding: '10px 20px', backgroundColor: '#ffffff', border: '1px solid #e0e0e0', borderRadius: '4px', cursor: 'pointer', color: '#2c3e50', fontWeight: 'bold', textDecoration: 'none', display: 'inline-block' },
};
