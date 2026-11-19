// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import en from './en.json';
import gu from './gu.json';
import hi from './hi.json';
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

  it('has Hindi only for English keys, with the same {placeholders}', () => {
    const marks = (text) => (text.match(/\{\w+\}/g) ?? []).sort();
    for (const [key, text] of Object.entries(hi)) {
      expect(en[key], key).toBeDefined();
      expect(marks(text), key).toEqual(marks(en[key]));
    }
  });

  it('shows English where Hindi has no text yet', () => {
    const missing = Object.keys(en).find((key) => !(key in hi));
    expect(translate('hi', missing)).toBe(en[missing]);
    expect(translate('hi', 'login.title')).toBe(hi['login.title']);
  });

  it('switches to Hindi', () => {
    render(<I18nProvider><Title /><LanguageSwitch /></I18nProvider>);
    fireEvent.click(screen.getByRole('button', { name: 'हि' }));
    expect(screen.getByRole('heading')).toHaveTextContent(hi['login.title']);
    expect(localStorage.getItem('pos.lang')).toBe('hi');
    expect(document.documentElement.lang).toBe('hi');
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
