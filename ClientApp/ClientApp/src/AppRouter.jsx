import React from 'react';
import { BrowserRouter, Routes, Route, NavLink, Navigate, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './AuthContext';
import { I18nProvider, LanguageSwitch, useT } from './i18n/I18nContext';
import { ToastProvider } from './ui/ToastContext';
import RequireRole from './components/RequireRole';
import POSMenu from './components/POSMenu';
import KitchenDisplay from './components/KitchenDisplay';
import OrderHistory from './components/OrderHistory';
import Login from './components/Login';
import StaffPage from './admin/StaffPage';
import MenuPage from './admin/MenuPage';
import TablesPage from './admin/TablesPage';
import StorePage from './admin/StorePage';
import SettingsPage from './admin/SettingsPage';
import StoreBrand from './ui/StoreBrand';

const POS_ROLES = ['cashier', 'manager', 'admin'];
const KITCHEN_ROLES = ['kitchen', 'manager', 'admin'];
const ADMIN_ROLES = ['admin'];
const MENU_ROLES = ['admin', 'manager'];
const ROLE_ORDER = ['admin', 'manager', 'cashier', 'kitchen'];

function HomeRedirect() {
  const { user, loading } = useAuth();
  if (loading) return null;
  if (!user) return <Navigate to="/login" replace />;
  const kitchenOnly = user.roles.length === 1 && user.roles[0] === 'kitchen';
  return <Navigate to={kitchenOnly ? '/kitchen' : '/pos'} replace />;
}

function AppHeader() {
  const { user, logout } = useAuth();
  const t = useT();
  if (!user) return null;
  const can = (roles) => roles.some((r) => user.roles.includes(r));
  const mainRole = ROLE_ORDER.find((r) => user.roles.includes(r));
  return (
    <header className="pos-header">
      <StoreBrand />
      <nav className="pos-nav" aria-label="Main">
        {can(POS_ROLES) && <NavLink to="/pos">{t('nav.pos')}</NavLink>}
        {can(POS_ROLES) && <NavLink to="/orders">{t('nav.orders')}</NavLink>}
        {can(KITCHEN_ROLES) && <NavLink to="/kitchen">{t('nav.kitchen')}</NavLink>}
        {can(MENU_ROLES) && <NavLink to="/admin/menu">{t('nav.menu')}</NavLink>}
        {can(MENU_ROLES) && <NavLink to="/admin/tables">{t('nav.tables')}</NavLink>}
        {can(ADMIN_ROLES) && <NavLink to="/admin/staff">{t('nav.staff')}</NavLink>}
        {can(ADMIN_ROLES) && <NavLink to="/admin/store">{t('nav.store')}</NavLink>}
        {can(ADMIN_ROLES) && <NavLink to="/admin/settings">{t('nav.settings')}</NavLink>}
      </nav>
      <div className="pos-user">
        {mainRole && <span className="pos-role" title={user.email}>{t(`role.${mainRole}`)}</span>}
        <LanguageSwitch />
        <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={logout}>{t('nav.signOut')}</button>
      </div>
    </header>
  );
}

function Layout() {
  const { pathname } = useLocation();
  const hideHeader = pathname.startsWith('/kitchen') || pathname.startsWith('/login');
  return (
    <>
      {!hideHeader && <AppHeader />}
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/" element={<HomeRedirect />} />
        <Route path="/pos" element={<RequireRole roles={POS_ROLES}><POSMenu /></RequireRole>} />
        <Route path="/orders" element={<RequireRole roles={POS_ROLES}><OrderHistory /></RequireRole>} />
        <Route path="/kitchen" element={<RequireRole roles={KITCHEN_ROLES}><KitchenDisplay /></RequireRole>} />
        <Route path="/admin/menu" element={<RequireRole roles={MENU_ROLES}><MenuPage /></RequireRole>} />
        <Route path="/admin/staff" element={<RequireRole roles={ADMIN_ROLES}><StaffPage /></RequireRole>} />
        <Route path="/admin/tables" element={<RequireRole roles={MENU_ROLES}><TablesPage /></RequireRole>} />
        <Route path="/admin/store" element={<RequireRole roles={ADMIN_ROLES}><StorePage /></RequireRole>} />
        <Route path="/admin/settings" element={<RequireRole roles={ADMIN_ROLES}><SettingsPage /></RequireRole>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <I18nProvider>
        <AuthProvider>
          <ToastProvider>
            <Layout />
          </ToastProvider>
        </AuthProvider>
      </I18nProvider>
    </BrowserRouter>
  );
}
