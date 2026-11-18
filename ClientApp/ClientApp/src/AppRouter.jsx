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
import MenuScanPage from './admin/MenuScanPage';
import TablesPage from './admin/TablesPage';
import StorePage from './admin/StorePage';
import SettingsPage from './admin/SettingsPage';
import BillingSettingsPage from './admin/BillingSettingsPage';
import PlatformPage from './admin/PlatformPage';
import StoreBrand from './ui/StoreBrand';
import { PrintBillPage, PrintKotPage, PrintDayPage, PrintCreditNotePage } from './print/PrintPages';
import DayClosePage from './components/DayClosePage';
import DayReportPage from './admin/DayReportPage';
import OfflineBanner from './offline/OfflineBanner';
import LicenceBanner from './licence/LicenceBanner';
import BackupBanner from './licence/BackupBanner';
import LicencePage from './admin/LicencePage';
import ShopLicencesPage from './admin/ShopLicencesPage';
import PlatformSupportPage from './admin/PlatformSupportPage';
import AboutPage from './about/AboutPage';
import AppFooter from './about/AppFooter';

const POS_ROLES = ['cashier', 'manager', 'admin'];
const KITCHEN_ROLES = ['kitchen', 'manager', 'admin'];
const ADMIN_ROLES = ['admin'];
const MENU_ROLES = ['admin', 'manager'];
const PLATFORM_ROLES = ['platform'];
const ROLE_ORDER = ['platform', 'admin', 'manager', 'cashier', 'kitchen'];

function HomeRedirect() {
  const { user, loading } = useAuth();
  if (loading) return null;
  if (!user) return <Navigate to="/login" replace />;
  if (user.roles.includes('platform')) return <Navigate to="/platform" replace />;
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
    <>
    <header className="pos-header">
      <StoreBrand />
      <nav className="pos-nav" aria-label="Main">
        {can(POS_ROLES) && <NavLink to="/pos">{t('nav.pos')}</NavLink>}
        {can(POS_ROLES) && <NavLink to="/orders">{t('nav.orders')}</NavLink>}
        {can(POS_ROLES) && <NavLink to="/day-close">{t('nav.dayClose')}</NavLink>}
        {can(KITCHEN_ROLES) && <NavLink to="/kitchen">{t('nav.kitchen')}</NavLink>}
        {can(MENU_ROLES) && <NavLink to="/admin/menu">{t('nav.menu')}</NavLink>}
        {can(MENU_ROLES) && <NavLink to="/admin/tables">{t('nav.tables')}</NavLink>}
        {can(ADMIN_ROLES) && <NavLink to="/admin/staff">{t('nav.staff')}</NavLink>}
        {can(ADMIN_ROLES) && <NavLink to="/admin/store">{t('nav.store')}</NavLink>}
        {can(ADMIN_ROLES) && <NavLink to="/admin/settings">{t('nav.settings')}</NavLink>}
        {can(ADMIN_ROLES) && <NavLink to="/admin/billing">{t('nav.billing')}</NavLink>}
        {can(ADMIN_ROLES) && <NavLink to="/admin/licence">{t('nav.licence')}</NavLink>}
        {can(PLATFORM_ROLES) && <NavLink to="/platform" end>{t('nav.platform')}</NavLink>}
        {can(PLATFORM_ROLES) && <NavLink to="/platform/licences">{t('nav.licences')}</NavLink>}
        {can(PLATFORM_ROLES) && <NavLink to="/platform/support">{t('nav.support')}</NavLink>}
      </nav>
      <div className="pos-user">
        {mainRole && <span className="pos-role" title={user.email}>{t(`role.${mainRole}`)}</span>}
        <NavLink to="/about" className="btn btn-outline-secondary btn-sm px-2" title={t('about.title')} aria-label={t('about.title')}>?</NavLink>
        <LanguageSwitch />
        <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={logout}>{t('nav.signOut')}</button>
      </div>
    </header>
    {can(POS_ROLES) && <OfflineBanner />}
    {!can(PLATFORM_ROLES) && <LicenceBanner />}
    {can(ADMIN_ROLES) && <BackupBanner />}
    </>
  );
}

function Layout() {
  const { pathname } = useLocation();
  const hideHeader = pathname.startsWith('/kitchen') || pathname.startsWith('/login') || pathname.startsWith('/print');
  // The kitchen screen and printouts stay clean; every other screen shows the support line at the bottom.
  // The POS screen fills the window (menu and order side by side), so the footer would push the order buttons off screen.
  const hideFooter = pathname.startsWith('/kitchen') || pathname.startsWith('/print') || pathname.startsWith('/pos');
  return (
    <>
      {!hideHeader && <AppHeader />}
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/" element={<HomeRedirect />} />
        <Route path="/pos" element={<RequireRole roles={POS_ROLES}><POSMenu /></RequireRole>} />
        <Route path="/orders" element={<RequireRole roles={POS_ROLES}><OrderHistory /></RequireRole>} />
        <Route path="/day-close" element={<RequireRole roles={POS_ROLES}><DayClosePage /></RequireRole>} />
        <Route path="/reports/day" element={<RequireRole roles={MENU_ROLES}><DayReportPage /></RequireRole>} />
        <Route path="/kitchen" element={<RequireRole roles={KITCHEN_ROLES}><KitchenDisplay /></RequireRole>} />
        <Route path="/admin/menu" element={<RequireRole roles={MENU_ROLES}><MenuPage /></RequireRole>} />
        <Route path="/admin/menu/scan" element={<RequireRole roles={MENU_ROLES}><MenuScanPage /></RequireRole>} />
        <Route path="/admin/staff" element={<RequireRole roles={ADMIN_ROLES}><StaffPage /></RequireRole>} />
        <Route path="/admin/tables" element={<RequireRole roles={MENU_ROLES}><TablesPage /></RequireRole>} />
        <Route path="/admin/store" element={<RequireRole roles={ADMIN_ROLES}><StorePage /></RequireRole>} />
        <Route path="/admin/settings" element={<RequireRole roles={ADMIN_ROLES}><SettingsPage /></RequireRole>} />
        <Route path="/admin/billing" element={<RequireRole roles={ADMIN_ROLES}><BillingSettingsPage /></RequireRole>} />
        <Route path="/admin/licence" element={<RequireRole roles={ADMIN_ROLES}><LicencePage /></RequireRole>} />
        <Route path="/platform" element={<RequireRole roles={PLATFORM_ROLES}><PlatformPage /></RequireRole>} />
        <Route path="/platform/licences" element={<RequireRole roles={PLATFORM_ROLES}><ShopLicencesPage /></RequireRole>} />
        <Route path="/platform/support" element={<RequireRole roles={PLATFORM_ROLES}><PlatformSupportPage /></RequireRole>} />
        <Route path="/about" element={<AboutPage />} />
        <Route path="/print/bill/:id" element={<RequireRole roles={POS_ROLES}><PrintBillPage /></RequireRole>} />
        <Route path="/print/kot/:id" element={<RequireRole roles={[...POS_ROLES, 'kitchen']}><PrintKotPage /></RequireRole>} />
        <Route path="/print/day" element={<RequireRole roles={MENU_ROLES}><PrintDayPage /></RequireRole>} />
        <Route path="/print/credit-note/:id" element={<RequireRole roles={POS_ROLES}><PrintCreditNotePage /></RequireRole>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      {!hideFooter && <AppFooter />}
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
