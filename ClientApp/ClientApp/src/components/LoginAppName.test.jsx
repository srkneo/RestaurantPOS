// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import { AuthProvider } from '../AuthContext';
import { I18nProvider } from '../i18n/I18nContext';
import Login from './Login';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

// v2.4 RN1: the sign-in page shows the new app name in every language.
describe('Login app name', () => {
  for (const lang of ['en', 'gu', 'hi']) {
    it(`shows AllInOne POS in ${lang}`, async () => {
      vi.stubGlobal('fetch', vi.fn(async () => ({ ok: false, json: async () => null })));
      render(<MemoryRouter><I18nProvider initialLang={lang}><AuthProvider><Login /></AuthProvider></I18nProvider></MemoryRouter>);
      expect(await screen.findByText('AllInOne POS')).toBeInTheDocument();
      expect(screen.queryByText('Restaurant POS')).toBeNull();
      expect(screen.queryByText('રેસ્ટોરન્ટ POS')).toBeNull();
      expect(screen.queryByText('रेस्टोरेंट POS')).toBeNull();
    });
  }
});
