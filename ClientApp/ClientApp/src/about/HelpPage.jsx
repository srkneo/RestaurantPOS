import React from 'react';
import { useI18n } from '../i18n/I18nContext';
import { HELP } from './helpContent';

// How-to pages for staff in the language chosen on screen (English, Gujarati or Hindi).
export default function HelpPage() {
  const { lang, t } = useI18n();
  const sections = HELP[lang] ?? HELP.en;
  return (
    <main className="pos-page">
      <div className="pos-page-head"><h1>{t('help.title')}</h1></div>
      <nav className="pos-card p-3 mb-3" aria-label={t('help.contents')}>
        <ol className="mb-0">
          {sections.map((s) => <li key={s.id}><a href={`#help-${s.id}`}>{s.title}</a></li>)}
        </ol>
      </nav>
      {sections.map((s) => (
        <section key={s.id} id={`help-${s.id}`} className="pos-card p-3 mb-3" aria-labelledby={`help-${s.id}-title`}>
          <h2 id={`help-${s.id}-title`} className="h5">{s.title}</h2>
          <ol className="mb-0">{s.steps.map((step) => <li key={step}>{step}</li>)}</ol>
        </section>
      ))}
    </main>
  );
}
