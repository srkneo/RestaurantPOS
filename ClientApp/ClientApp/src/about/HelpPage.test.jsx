// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import HelpPage from './HelpPage';
import { HELP } from './helpContent';

afterEach(() => cleanup());

describe('Help page', () => {
  it('shows the how-to sections in English', () => {
    render(<I18nProvider initialLang="en"><HelpPage /></I18nProvider>);
    expect(screen.getByRole('heading', { level: 1, name: 'Help' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'QR ordering at the table' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Day close' })).toHaveAttribute('href', '#help-dayclose');
  });

  it('shows the same sections in Gujarati', () => {
    render(<I18nProvider initialLang="gu"><HelpPage /></I18nProvider>);
    expect(screen.getByRole('region', { name: 'દિવસ બંધ' })).toBeInTheDocument();
    expect(HELP.gu.map((s) => s.id)).toEqual(HELP.en.map((s) => s.id));
  });
});
