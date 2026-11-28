// UI1: turns one audit row into readable text for the Activity log screen.
// Details is JSON text written by the server: { changes: { Field: [before, after] } }, { support: "..." },
// { approval: "..." } or any other object. Text that is not JSON is shown as it is.
const show = (value) => {
  if (value === null || value === undefined || value === '') return '–';
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
};

export function describeDetails(details) {
  if (details === null || details === undefined || details === '') return '';
  let data;
  try {
    data = JSON.parse(details);
  } catch {
    return String(details);
  }
  if (data === null || typeof data !== 'object' || Array.isArray(data)) return show(data);
  if (data.changes && typeof data.changes === 'object' && !Array.isArray(data.changes)) {
    return Object.entries(data.changes).map(([field, pair]) => {
      const [before, after] = Array.isArray(pair) ? pair : [null, pair];
      if (before === null || before === undefined) return `${field}: ${show(after)}`;
      if (after === null || after === undefined) return `${field}: ${show(before)}`;
      return `${field}: ${show(before)} → ${show(after)}`;
    }).join('; ');
  }
  if (typeof data.support === 'string') return data.support;
  if (typeof data.approval === 'string') return data.approval;
  return Object.entries(data).map(([key, value]) => `${key}: ${show(value)}`).join('; ');
}

// MenuItem -> Menu item, KitchenOrder -> Kitchen order, Support -> Support.
export function typeName(type) {
  if (!type) return '';
  const words = String(type).replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}
