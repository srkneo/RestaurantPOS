import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import en from './en.json';
import gu from './gu.json';

export const LANGUAGES = [
  { code: 'en', label: 'EN', name: 'English' },
  { code: 'gu', label: 'ગુ', name: 'ગુજરાતી' },
];

const dictionaries = { en, gu };
const STORAGE_KEY = 'pos.lang';

function readStoredLang() {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return dictionaries[value] ? value : 'en';
  } catch {
    return 'en';
  }
}

// Text for a key in the given language; falls back to English, then to the key itself.
// Values like {token} are filled from vars.
export function translate(lang, key, vars) {
  const text = dictionaries[lang]?.[key] ?? en[key] ?? key;
  if (!vars) return text;
  return text.replace(/\{(\w+)\}/g, (match, name) => (vars[name] ?? match));
}

const I18nContext = createContext({
  lang: 'en',
  setLang: () => {},
  t: (key, vars) => translate('en', key, vars),
});

export function I18nProvider({ children, initialLang }) {
  const [lang, setLangState] = useState(() => initialLang ?? readStoredLang());

  useEffect(() => {
    document.documentElement.lang = lang;
  }, [lang]);

  const setLang = useCallback((next) => {
    if (!dictionaries[next]) return;
    setLangState(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // storage can be blocked; the choice then lasts only for this visit
    }
  }, []);

  const value = useMemo(() => ({ lang, setLang, t: (key, vars) => translate(lang, key, vars) }), [lang, setLang]);
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export const useI18n = () => useContext(I18nContext);
export const useT = () => useContext(I18nContext).t;

export function LanguageSwitch() {
  const { lang, setLang, t } = useI18n();
  return (
    <div className="pos-lang" role="group" aria-label={t('language.label')}>
      {LANGUAGES.map((l) => (
        <button key={l.code} type="button" title={l.name} aria-pressed={lang === l.code} onClick={() => setLang(l.code)}>
          {l.label}
        </button>
      ))}
    </div>
  );
}
