import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { loadAbout, versionText } from './about';
import './ServerWatch.css';

// Watches the POS server.
// On a shop PC, when the server stops answering (an update or a restart), a full-screen notice covers the screen until
// it is back, so nothing is tapped by mistake. (On the cloud the offline order queue takes over instead.)
// Everywhere, a bar offers a reload when a new version is running. It never reloads by itself, so an order being
// typed is not lost.
export default function ServerWatch({ checkEvery = 10000, downAfter = 2, versionEvery = 300000 }) {
  const t = useT();
  const [shopPc, setShopPc] = useState(false);
  const [down, setDown] = useState(false);
  const [newVersion, setNewVersion] = useState(null);
  const started = useRef(null);
  const failures = useRef(0);
  const isDown = useRef(false);

  const checkVersion = useCallback(async () => {
    const about = await loadAbout(true);
    if (!about?.version) return;
    if (started.current === null) started.current = about.version;
    else if (about.version !== started.current) setNewVersion(about.version);
  }, []);

  useEffect(() => {
    let alive = true;
    loadAbout().then((about) => {
      if (!alive || !about) return;
      if (started.current === null && about.version) started.current = about.version;
      setShopPc(about.shopPc === true);
    });
    const timer = setInterval(checkVersion, versionEvery);
    return () => {
      alive = false;
      clearInterval(timer);
    };
  }, [checkVersion, versionEvery]);

  useEffect(() => {
    if (!shopPc) return undefined;
    let alive = true;
    const check = async () => {
      let ok = false;
      const stop = new AbortController();
      const timeout = setTimeout(() => stop.abort(), 5000);
      try {
        const res = await fetch('/health', { cache: 'no-store', signal: stop.signal });
        ok = res.ok;
      } catch {
        ok = false;
      } finally {
        clearTimeout(timeout);
      }
      if (!alive) return;
      if (ok) {
        failures.current = 0;
        if (isDown.current) {
          isDown.current = false;
          setDown(false);
          checkVersion();
        }
      } else {
        failures.current += 1;
        if (failures.current >= downAfter && !isDown.current) {
          isDown.current = true;
          setDown(true);
        }
      }
    };
    const timer = setInterval(check, checkEvery);
    return () => {
      alive = false;
      clearInterval(timer);
    };
  }, [shopPc, checkEvery, downAfter, checkVersion]);

  return (
    <>
      {newVersion && !down && (
        <div className="alert alert-info rounded-0 mb-0 py-2 d-flex flex-wrap align-items-center gap-2 d-print-none" role="status">
          <span>{t('watch.newVersion', { version: versionText(newVersion) })}</span>
          <button type="button" className="btn btn-sm btn-outline-dark" onClick={() => window.location.reload()}>{t('watch.reload')}</button>
        </div>
      )}
      {down && (
        <div className="pos-server-down d-print-none" role="alertdialog" aria-modal="true" aria-labelledby="pos-server-down-title">
          <div className="pos-server-down-card">
            <div className="spinner-border" aria-hidden="true" />
            <h2 id="pos-server-down-title">{t('watch.downTitle')}</h2>
            <p className="mb-0">{t('watch.downText')}</p>
          </div>
        </div>
      )}
    </>
  );
}
