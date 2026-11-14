// Small shared helpers for money, ids and server times.

export const formatMoney = (value) => '₹' + Number(value ?? 0).toFixed(2);

// Id that makes "place order" safe to retry. crypto.randomUUID only exists on https/localhost.
export function newClientId() {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

// Server times are UTC; a value without a zone is read as UTC, not local time.
export function parseServerDate(value) {
  if (!value) return null;
  const text = String(value);
  const hasZone = /[zZ]$|[+-]\d\d:?\d\d$/.test(text);
  const date = new Date(hasZone ? text : text + 'Z');
  return Number.isNaN(date.getTime()) ? null : date;
}

export function minutesSince(value, now = Date.now()) {
  const date = parseServerDate(value);
  if (!date) return 0;
  return Math.max(0, Math.floor((now - date.getTime()) / 60000));
}

// Kitchen ticket label. Later these limits become outlet settings.
export const WAITING_AFTER_MIN = 5;
export const LATE_AFTER_MIN = 10;

export function kitchenStage(minutes) {
  if (minutes >= LATE_AFTER_MIN) return 'late';
  if (minutes >= WAITING_AFTER_MIN) return 'waiting';
  return 'new';
}
