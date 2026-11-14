// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import en from './en.json';
import gu from './gu.json';
import { I18nProvider, LanguageSwitch, translate, useT } from './I18nContext';

function Title() {
  const t = useT();
  return <h1>{t('login.title')}</h1>;
}

afterEach(() => {
  cleanup();
  localStorage.clear();
});

describe('i18n', () => {
  it('has a Gujarati text for every English key', () => {
    expect(Object.keys(gu).sort()).toEqual(Object.keys(en).sort());
  });

  it('falls back to the key when a text is missing', () => {
    expect(translate('en', 'missing.key')).toBe('missing.key');
    expect(translate('gu', 'login.title')).toBe(gu['login.title']);
  });

  it('switches language and remembers the choice', () => {
    render(<I18nProvider><Title /><LanguageSwitch /></I18nProvider>);
    expect(screen.getByRole('heading')).toHaveTextContent('Sign in');

    fireEvent.click(screen.getByRole('button', { name: 'ગુ' }));

    expect(screen.getByRole('heading')).toHaveTextContent(gu['login.title']);
    expect(localStorage.getItem('pos.lang')).toBe('gu');
    expect(document.documentElement.lang).toBe('gu');
  });
});
