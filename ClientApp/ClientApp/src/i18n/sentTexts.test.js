// @vitest-environment node
import { describe, it, expect } from 'vitest';
import en from './en.json';
import gu from './gu.json';
import hi from './hi.json';

// v2.4 RN2: texts the app sends (WhatsApp demo request) use the new name; no text in any language keeps an old app name.
const OLD_NAMES = ['restaurant pos', 'restaurantpos', 'રેસ્ટોરન્ટ pos', 'रेस्टोरेंट pos'];

describe('Sent texts', () => {
  it('the WhatsApp demo request names AllInOne POS in every language', () => {
    expect(en['pricing.demoText']).toBe('Hello, I would like a demo of AllInOne POS.');
    expect(gu['pricing.demoText']).toContain('AllInOne POS');
    expect(hi['pricing.demoText']).toContain('AllInOne POS');
  });

  it('no text in any language still says an old app name', () => {
    for (const dict of [en, gu, hi]) {
      for (const [key, value] of Object.entries(dict)) {
        for (const old of OLD_NAMES) expect(`${key}: ${String(value).toLowerCase()}`).not.toContain(old);
      }
    }
  });

  it('the demo text is not empty and has no placeholder left', () => {
    for (const dict of [en, gu, hi]) {
      expect(dict['pricing.demoText'].trim().length).toBeGreaterThan(10);
      expect(dict['pricing.demoText']).not.toMatch(/[{}]/);
    }
  });
});
