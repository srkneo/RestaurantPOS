import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { loadAbout, telLink, versionText, whatsAppLink } from './about';
import '../components/OrderHistory.css';

// Who to call for help, the version, and (on a shop PC) the licence and a short PC code. "Copy details" gives
// the owner one text to paste into WhatsApp when asking for support.
export default function AboutPage() {
  const t = useT();
  const { showToast } = useToast();
  const [about, setAbout] = useState(null);
  const [loadState, setLoadState] = useState('loading');

  const load = useCallback(async () => {
    setLoadState('loading');
    const data = await loadAbout(true);
    if (data) {
      setAbout(data);
      setLoadState('ready');
    } else {
      setLoadState('error');
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const rows = about
    ? [
        ['about.company', about.companyName],
        ['about.version', versionText(about.version)],
        ['about.phone', about.phone],
        ['about.whatsAppNumber', about.whatsApp],
        ['about.email', about.email],
        ['about.hours', about.hours],
        ['about.website', about.website],
        ['about.licenceCustomer', about.licenceCustomer],
        ['about.licencePlan', about.licencePlan ? t(`licence.plan.${about.licencePlan}`) : null],
        ['about.deviceCode', about.deviceCode],
      ].filter(([, value]) => value)
    : [];

  const copy = async () => {
    const text = [about.productName, ...rows.map(([key, value]) => `${t(key)}: ${value}`)].join('\n');
    try {
      await navigator.clipboard.writeText(text);
      showToast(t('about.copied'));
    } catch {
      showToast(t('about.copyFailed'), 'error');
    }
  };

  const whatsApp = about ? whatsAppLink(about.whatsApp || about.phone) : null;

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('about.title')}</h1>
      </div>
      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && (
        <div className="d-flex flex-column gap-3" style={{ maxWidth: 640 }}>
          <div className="pos-card">
            <h2 className="h5 mb-1">{about.productName}</h2>
            <p className="text-body-secondary mb-3">{t('about.madeBy', { company: about.companyName })}</p>
            <dl className="row mb-0">
              {rows.map(([key, value]) => (
                <React.Fragment key={key}>
                  <dt className="col-sm-5">{t(key)}</dt>
                  <dd className="col-sm-7">{value}</dd>
                </React.Fragment>
              ))}
            </dl>
          </div>
          {!about.phone && !about.email && <p className="text-body-secondary mb-0">{t('about.noContact')}</p>}
          <div className="d-flex flex-wrap gap-2">
            {about.phone && <a className="btn btn-outline-secondary px-4" href={telLink(about.phone)}>{t('about.call')}</a>}
            {whatsApp && <a className="btn btn-success px-4" href={whatsApp} target="_blank" rel="noreferrer">{t('about.whatsApp')}</a>}
            {about.email && <a className="btn btn-outline-secondary px-4" href={`mailto:${about.email}`}>{t('about.sendEmail')}</a>}
            <button type="button" className="btn btn-primary px-4" onClick={copy}>{t('about.copy')}</button>
          </div>
        </div>
      )}
    </main>
  );
}
