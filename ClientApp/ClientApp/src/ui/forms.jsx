import React from 'react';

// Shared helpers for owner forms.

// Turns a ValidationProblem response into { field: message } with lower-case field names.
export async function readErrors(res) {
  try {
    const data = await res.json();
    if (data?.errors) {
      return Object.fromEntries(Object.entries(data.errors).map(([k, v]) => [k.toLowerCase(), Array.isArray(v) ? v.join(' ') : String(v)]));
    }
    return { general: data?.message || data?.title || '' };
  } catch {
    return { general: '' };
  }
}

export function send(url, method, body) {
  return fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
}

// Label, the field itself (children), an optional hint and the error message under it.
export function Field({ id, label, error, hint, children }) {
  return (
    <div>
      <label htmlFor={id} className="form-label">{label}</label>
      {children}
      {hint && !error && <p className="pos-hint">{hint}</p>}
      {error && <div className="invalid-feedback d-block">{error}</div>}
    </div>
  );
}
