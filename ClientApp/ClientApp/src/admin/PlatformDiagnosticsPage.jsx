import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import Icon from '../ui/Icon';
import '../components/OrderHistory.css';

// Server times come in UTC; without a zone mark the browser would read them as local time.
const asDate = (value) => (value ? new Date(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z') : null);

function Row({ label, children }) {
  return (
    <div className="d-flex justify-content-between gap-3 py-2 border-bottom">
      <span className="text-body-secondary">{label}</span>
      <strong>{children}</strong>
    </div>
  );
}

// OB9: the platform owner's quick health check of the server.
export default function PlatformDiagnosticsPage() {
  const t = useT();
  const [data, setData] = useState(null);
  const [state, setState] = useState('loading');

  const load = useCallback(async () => {
    setState('loading');
    try {
      const res = await fetch('/api/platform/diagnostics');
      if (!res.ok) { setState('error'); return; }
      setData(await res.json());
      setState('ready');
    } catch {
      setState('error');
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const onOff = (on) => (on ? t('diag.on') : t('diag.off'));
  const hours = data ? Math.floor(data.uptimeSeconds / 3600) : 0;
  const minutes = data ? Math.floor((data.uptimeSeconds % 3600) / 60) : 0;

  return (
    <main className="pos-page">
      <div className="pos-page-head d-flex justify-content-between align-items-center gap-2">
        <h1>{t('diag.title')}</h1>
        <button type="button" className="btn btn-outline-secondary" onClick={load} disabled={state === 'loading'}>
          <Icon name="rotate-left" className="me-1" />{t('diag.refresh')}
        </button>
      </div>
      {state === 'loading' && !data && <StateMessage kind="loading" />}
      {state === 'error' && <StateMessage kind="error" onRetry={load} />}
      {data && state !== 'error' && (
        <div className="d-flex flex-column gap-3">
          <section className="pos-card p-3 p-md-4" style={{ maxWidth: 640 }}>
            <Row label={t('diag.version')}>{data.version}</Row>
            <Row label={t('diag.uptime')}>{t('diag.uptimeValue', { hours, minutes })}</Row>
            <Row label={t('diag.database')}>
              <span className={data.databaseOk ? 'text-success' : 'text-danger'}>{data.databaseOk ? t('diag.ok') : t('diag.problem')}</span>
            </Row>
            <Row label={t('diag.sentry')}>{onOff(data.sentryOn)}</Row>
            <Row label={t('diag.telemetry')}>{onOff(data.telemetryOn)}</Row>
            <Row label={t('diag.restaurants')}>{data.restaurants}</Row>
            <Row label={t('diag.failedSignIns')}>
              <span className={data.failedSignIns24h > 20 ? 'text-danger' : undefined}>{data.failedSignIns24h}</span>
            </Row>
          </section>
          <section className="pos-card p-3 p-md-4">
            <h2 className="h5">{t('diag.recent')}</h2>
            {data.recentEvents.length === 0 ? (
              <p className="text-body-secondary mb-0">{t('diag.noEvents')}</p>
            ) : (
              <div className="table-responsive">
                <table className="table table-sm align-middle mb-0">
                  <thead>
                    <tr>
                      <th>{t('diag.time')}</th>
                      <th>{t('diag.event')}</th>
                      <th>{t('diag.result')}</th>
                      <th>IP</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.recentEvents.map((e, i) => (
                      <tr key={`${e.requestId ?? ''}-${i}`}>
                        <td>{asDate(e.atUtc)?.toLocaleString() ?? ''}</td>
                        <td>{e.type}</td>
                        <td className={e.success ? 'text-success' : 'text-danger'}>{e.success ? t('diag.ok') : t('diag.failed')}</td>
                        <td>{e.ip ?? ''}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>
      )}
    </main>
  );
}
