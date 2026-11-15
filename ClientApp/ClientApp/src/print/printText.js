import { translate } from '../i18n/I18nContext';
import { parseServerDate } from '../ui/format';

// Receipts use the outlet's print language, whatever language the cashier's screen shows.
export const printT = (lang) => (key, vars) => translate(lang, key, vars);

export const money = (value) => Number(value ?? 0).toFixed(2);

// Bills show India time, whatever the device clock says.
export function billDateTime(value) {
  const date = parseServerDate(value);
  if (!date) return '';
  return new Intl.DateTimeFormat('en-IN', {
    timeZone: 'Asia/Kolkata', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: true,
  }).format(date);
}

// 2.5 -> "2.5", 9 -> "9" (half of the GST rate for CGST and SGST).
export const halfRate = (rate) => String(Number(rate) / 2);
