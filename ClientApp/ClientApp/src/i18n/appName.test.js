// @vitest-environment node
import { describe, it, expect } from 'vitest';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import en from './en.json';
import gu from './gu.json';
import hi from './hi.json';
import { translate } from './I18nContext';

// v2.4 RN1: the app name lives in one key (app.name) and is AllInOne POS everywhere.
const fromApp = (path) => fileURLToPath(new URL(`../../${path}`, import.meta.url));
const OLD_NAMES = ['Restaurant POS', 'RestaurantPOS', 'રેસ્ટોરન્ટ POS', 'रेस्टोरेंट POS'];

describe('App name', () => {
  it('is AllInOne POS in English, Gujarati and Hindi', () => {
    expect(en['app.name']).toBe('AllInOne POS');
    expect(gu['app.name']).toBe('AllInOne POS');
    expect(hi['app.name']).toBe('AllInOne POS');
    for (const lang of ['en', 'gu', 'hi']) expect(translate(lang, 'app.name')).toBe('AllInOne POS');
  });

  it('keeps the same keys in all three languages', () => {
    expect(Object.keys(gu).sort()).toEqual(Object.keys(en).sort());
    expect(Object.keys(hi).sort()).toEqual(Object.keys(en).sort());
  });

  it('no language still uses an old name for the app', () => {
    for (const dict of [en, gu, hi]) {
      for (const old of OLD_NAMES) expect(dict['app.name']).not.toContain(old);
    }
  });

  it('the browser tab title is AllInOne POS and the page links the manifest', () => {
    const html = readFileSync(fromApp('index.html'), 'utf8');
    expect(html).toContain('<title>AllInOne POS</title>');
    expect(html).toContain('<link rel="manifest" href="/manifest.webmanifest" />');
    for (const old of OLD_NAMES) expect(html).not.toContain(old);
  });

  it('the install manifest names the app and its icons exist', () => {
    const path = fromApp('public/manifest.webmanifest');
    expect(existsSync(path)).toBe(true);
    const text = readFileSync(path, 'utf8');
    const manifest = JSON.parse(text);
    expect(manifest.name).toBe('AllInOne POS');
    expect(manifest.short_name).toBe('AllInOne POS');
    expect(manifest.start_url).toBe('/');
    expect(manifest.icons.length).toBeGreaterThan(0);
    for (const icon of manifest.icons) expect(existsSync(fromApp(`public${icon.src}`))).toBe(true);
    for (const old of OLD_NAMES) expect(text).not.toContain(old);
  });

  it('a missing key falls back to English, then to the key, never to an old name', () => {
    expect(translate('xx', 'app.name')).toBe('AllInOne POS');
    expect(translate('gu', 'app.nameThatDoesNotExist')).toBe('app.nameThatDoesNotExist');
  });
});
