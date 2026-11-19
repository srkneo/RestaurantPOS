// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n/I18nContext';
import LegalPage from './LegalPage';
import { LEGAL } from './legalText';

afterEach(cleanup);

const show = (kind) => render(<MemoryRouter><I18nProvider><LegalPage kind={kind} /></I18nProvider></MemoryRouter>);

describe('Legal pages', () => {
  it('shows the terms with every section and the draft note', () => {
    show('terms');
    expect(screen.getByRole('heading', { level: 1, name: 'Terms of service' })).toBeInTheDocument();
    expect(screen.getAllByRole('heading', { level: 2 })).toHaveLength(LEGAL.terms.sections.length);
    expect(screen.getByRole('note')).toHaveTextContent('Draft for review');
  });

  it('shows the privacy policy', () => {
    show('privacy');
    expect(screen.getByRole('heading', { level: 1, name: 'Privacy policy' })).toBeInTheDocument();
    expect(screen.getByText(/Digital Personal Data Protection Act/)).toBeInTheDocument();
  });
});
