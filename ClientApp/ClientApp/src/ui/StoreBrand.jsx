import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import './StoreBrand.css';

// Restaurant name (and logo, if uploaded) in the header; falls back to the app name.
export default function StoreBrand() {
  const t = useT();
  const [store, setStore] = useState(null);

  useEffect(() => {
    let active = true;
    fetch('/api/store')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => { if (active) setStore(data); })
      .catch(() => {});
    return () => { active = false; };
  }, []);

  return (
    <span className="pos-brand pos-store-brand" title={store?.outletName || undefined}>
      {store?.hasLogo && <img src="/api/store/logo" alt="" />}
      <span className="pos-store-name">{store?.restaurantName || t('app.name')}</span>
    </span>
  );
}
