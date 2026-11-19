import React from 'react';
import { Link } from 'react-router-dom';
import { LanguageSwitch, useT } from '../i18n/I18nContext';
import { LEGAL } from './legalText';

// Public terms of service and privacy policy (cloud version). The wording is a draft until Vyom Solutions reviews it.
export default function LegalPage({ kind }) {
  const t = useT();
  const doc = LEGAL[kind] || LEGAL.terms;
  return (
    <main className="container py-4">
      <div className="col-lg-8 mx-auto">
        <div className="d-flex justify-content-between align-items-center gap-2 mb-3">
          <Link to="/signup">{t('legal.back')}</Link>
          <LanguageSwitch />
        </div>
        <p className="alert alert-warning" role="note">{t('legal.draft')}</p>
        <h1>{doc.title}</h1>
        <p className="text-muted">{t('legal.englishOnly')} · {doc.updated}</p>
        {doc.sections.map(([heading, paragraphs]) => (
          <section key={heading} className="mb-3">
            <h2 className="h5">{heading}</h2>
            {paragraphs.map((text) => <p key={text}>{text}</p>)}
          </section>
        ))}
        <p className="d-flex gap-3">
          <Link to="/terms">{t('legal.terms')}</Link>
          <Link to="/privacy">{t('legal.privacy')}</Link>
        </p>
      </div>
    </main>
  );
}
