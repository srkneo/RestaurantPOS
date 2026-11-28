// @vitest-environment node
import { describe, it, expect } from 'vitest';
import en from './en.json';
import gu from './gu.json';
import hi from './hi.json';
import { LEGAL } from '../components/legalText';

// v2.4 RN5: users see "business", not "restaurant". Code names, API paths and the {restaurant}
// placeholder stay. The outlet type "Restaurant with tables" stays, because a restaurant is one type of business.
const KEEP = ['settings.preset.dine-in'];
const OLD_WORD = { en: /restaurant/i, gu: /રેસ્ટોરન્ટ/, hi: /रेस्टोरेंट|रेस्तरां/ };
const DICTS = { en, gu, hi };
const words = (value) => String(value).replace(/\{restaurant\}/g, '');

describe('Business words', () => {
  it('English screens say business', () => {
    expect(en['nav.platform']).toBe('Businesses');
    expect(en['platform.title']).toBe('Businesses');
    expect(en['platform.add']).toBe('Add business');
    expect(en['platform.restaurant']).toBe('Business');
    expect(en['platform.restaurantName']).toBe('Business name');
    expect(en['store.restaurantName']).toBe('Business name');
    expect(en['signup.restaurantName']).toBe('Business name');
    expect(en['signup.link']).toBe('New business? Start a 30-day free trial');
    expect(en['platform.added']).toBe('Business added. The owner can sign in now.');
    expect(en['platform.empty']).toBe('No businesses yet.');
    expect(en['platform.search']).toBe('Search businesses');
    expect(en['platform.noMatch']).toBe('No business matches.');
    expect(en['platform.supportTitle']).toBe('Open this business as Support?');
    expect(en['diag.restaurants']).toBe('Businesses');
  });

  it('Gujarati and Hindi screens say business', () => {
    expect(gu['nav.platform']).toBe('બિઝનેસ');
    expect(gu['platform.add']).toBe('બિઝનેસ ઉમેરો');
    expect(gu['store.restaurantName']).toBe('બિઝનેસનું નામ');
    expect(hi['nav.platform']).toBe('व्यवसाय');
    expect(hi['platform.add']).toBe('व्यवसाय जोड़ें');
    expect(hi['store.restaurantName']).toBe('व्यवसाय का नाम');
  });

  it('no text in any language still says restaurant', () => {
    for (const [lang, dict] of Object.entries(DICTS)) {
      const left = Object.entries(dict).filter(([key, value]) => !KEEP.includes(key) && OLD_WORD[lang].test(words(value))).map(([key]) => `${lang}:${key}`);
      expect(left).toEqual([]);
    }
  });

  it('keeps the outlet type name, the placeholders and the same keys in all languages', () => {
    expect(en['settings.preset.dine-in']).toBe('Restaurant with tables');
    expect(en['share.billTitle']).toContain('{restaurant}');
    expect(Object.keys(gu).sort()).toEqual(Object.keys(en).sort());
    expect(Object.keys(hi).sort()).toEqual(Object.keys(en).sort());
  });

  it('the terms and privacy texts say business', () => {
    const text = JSON.stringify(LEGAL).replace(/RestaurantPOS/g, '');
    expect(text).not.toMatch(/restaurant/i);
    expect(text).toContain('every business that uses');
  });
});
