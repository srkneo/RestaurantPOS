// The last menu, tables and settings this till loaded, so the POS still opens when the server cannot be reached.
export function readMenuCache(key) {
  try {
    const value = localStorage.getItem(key);
    return value ? JSON.parse(value) : null;
  } catch {
    return null;
  }
}

export function writeMenuCache(key, data) {
  try {
    localStorage.setItem(key, JSON.stringify({ ...data, savedAt: new Date().toISOString() }));
  } catch {
    // storage full or blocked: the POS simply has no offline copy
  }
}
