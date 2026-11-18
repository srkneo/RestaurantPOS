import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { loadAbout, telLink, versionText } from './about';

// A small line at the bottom of the screens: product and version, company, support phone, and a link to About.
export default function AppFooter() {
  const t = useT();
  const [about, setAbout] = useState(null);

  useEffect(() => {
    let alive = true;
    loadAbout().then((data) => {
      if (alive) setAbout(data);
    });
    return () => {
      alive = false;
    };
  }, []);

  if (!about) return null;
  return (
    <footer className="text-center text-body-secondary small py-3 px-3 d-print-none">
      <span>{about.productName} {versionText(about.version)}</span>
      <span className="mx-2" aria-hidden="true">·</span>
      <span>© {new Date().getFullYear()} {about.companyName}</span>
      {about.phone && (
        <>
          <span className="mx-2" aria-hidden="true">·</span>
          <span>{t('about.support')}: <a href={telLink(about.phone)}>{about.phone}</a></span>
        </>
      )}
      <span className="mx-2" aria-hidden="true">·</span>
      <Link to="/about">{t('about.link')}</Link>
      <span className="mx-2" aria-hidden="true">·</span>
      <Link to="/help">{t('help.link')}</Link>
    </footer>
  );
}
