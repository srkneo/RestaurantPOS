import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import './StoreBrand.css';

// Restaurant name (and logo, if uploaded) in the header; falls back to the app name.
// If the server cannot be reached, it tries again when the browser is back online and every 30 seconds.
export default function StoreBrand() {
  const t = useT();
  const [store, setStore] = useState(null);

  useEffect(() => {
    let active = true;
    let timer = null;
    const load = () => {
      fetch('/api/store')
        .then((res) => {
          if ([502, 503, 504].includes(res.status)) throw new Error('unreachable');
          return res.ok ? res.json() : null;
        })
        .then((data) => {
          if (!active) return;
          setStore(data);
          clearInterval(timer);
          timer = null;
        })
        .catch(() => {
          if (active && !timer) timer = setInterval(load, 30000);
        });
    };
    load();
    window.addEventListener('online', load);
    return () => {
      active = false;
      clearInterval(timer);
      window.removeEventListener('online', load);
    };
  }, []);

  return (
    <span className="pos-brand pos-store-brand" title={store?.outletName || undefined}>
      {store?.hasLogo && <img src="/api/store/logo" alt="" />}
      <span className="pos-store-name">{store?.restaurantName || t('app.name')}</span>
    </span>
  );
}
