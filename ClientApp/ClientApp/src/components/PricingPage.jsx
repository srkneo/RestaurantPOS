import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { LanguageSwitch, useT } from '../i18n/I18nContext';

const FEATURES = ['f1', 'f2', 'f3', 'f4', 'f5', 'f6', 'f7', 'f8', 'f9'];
const rupees = (value) => '₹' + Number(value ?? 0).toLocaleString('en-IN', { maximumFractionDigits: 0 });
const getJson = (url) => fetch(url).then((res) => (res.ok ? res.json() : null)).catch(() => null);

// Public price list for the cloud version. Prices come from the server (Pricing:Monthly / Pricing:Yearly) and the
// WhatsApp and phone buttons from the support details the platform owner keeps on /platform.
export default function PricingPage() {
  const t = useT();
  const [plans, setPlans] = useState(null);
  const [about, setAbout] = useState(null);

  useEffect(() => {
    let alive = true;
    getJson('/api/signup/plans').then((data) => { if (alive) setPlans(data); });
    getJson('/api/about').then((data) => { if (alive) setAbout(data); });
    return () => { alive = false; };
  }, []);

  const digits = (about?.whatsApp || '').replace(/\D/g, '');
  const whatsApp = digits.length === 10 ? `91${digits}` : digits;
  const saving = plans ? plans.monthly * 12 - plans.yearly : 0;

  return (
    <main className="container py-4">
      <div className="col-lg-9 mx-auto">
        <div className="d-flex justify-content-between align-items-center gap-2 mb-3">
          <span className="pos-brand">{t('app.name')}</span>
          <LanguageSwitch />
        </div>
        <h1>{t('pricing.title')}</h1>
        {plans && <p className="lead">{t('pricing.trial', { days: plans.trialDays })}</p>}
        {plans && (
          <div className="row g-3 mb-3">
            <div className="col-md-6">
              <section className="card h-100">
                <div className="card-body">
                  <h2 className="h5">{t('pricing.monthly')}</h2>
                  <p className="fs-3 fw-bold mb-0">{t('pricing.perMonth', { price: rupees(plans.monthly) })}</p>
                </div>
              </section>
            </div>
            <div className="col-md-6">
              <section className="card h-100 border-success">
                <div className="card-body">
                  <h2 className="h5">{t('pricing.yearly')}</h2>
                  <p className="fs-3 fw-bold mb-0">{t('pricing.perYear', { price: rupees(plans.yearly) })}</p>
                  {saving > 0 && <p className="text-success mb-0">{t('pricing.saving', { amount: rupees(saving) })}</p>}
                </div>
              </section>
            </div>
          </div>
        )}
        <p className="text-muted">{t('pricing.payBy')}</p>
        <h2 className="h5">{t('pricing.includes')}</h2>
        <ul>
          {FEATURES.map((f) => <li key={f}>{t(`pricing.${f}`)}</li>)}
        </ul>
        <div className="d-flex flex-wrap gap-2 my-3">
          {plans?.signupOpen && <Link to="/signup" className="btn btn-primary btn-lg">{t('pricing.start')}</Link>}
          {whatsApp && (
            <a className="btn btn-success btn-lg" target="_blank" rel="noreferrer"
              href={`https://wa.me/${whatsApp}?text=${encodeURIComponent(t('pricing.demoText'))}`}>{t('pricing.demo')}</a>
          )}
          {about?.phone && (
            <a className="btn btn-outline-secondary btn-lg" href={`tel:${about.phone.replace(/[^\d+]/g, '')}`}>{t('pricing.call', { phone: about.phone })}</a>
          )}
        </div>
        <p>{t('pricing.shopPc')}</p>
        <p className="d-flex flex-wrap gap-3">
          <Link to="/login">{t('pricing.signIn')}</Link>
          <Link to="/terms">{t('legal.terms')}</Link>
          <Link to="/privacy">{t('legal.privacy')}</Link>
        </p>
      </div>
    </main>
  );
}
