import React from 'react';

// Shared helpers for owner forms.

// ERR1: the request ID the server put on its answer (body requestId or the X-Request-ID header), if it looks safe.
export function errorId(res, data) {
  const fromBody = data && typeof data.requestId === 'string' ? data.requestId : null;
  let fromHeader = null;
  try {
    fromHeader = res?.headers?.get?.('X-Request-ID') ?? null;
  } catch {
    fromHeader = null;
  }
  const id = fromBody || fromHeader;
  return id && /^[A-Za-z0-9._-]{1,64}$/.test(id) ? id : null;
}

// Only server failures (500 and up) get the ID; a form mistake needs no support call.
function withErrorId(text, res, data) {
  if (!res || !(res.status >= 500)) return text;
  const id = errorId(res, data);
  if (!id) return text;
  return text ? `${text} [ID: ${id}]` : `[ID: ${id}]`;
}

// Turns a ValidationProblem response into { field: message } with lower-case field names.
export async function readErrors(res) {
  try {
    const data = await res.json();
    if (data?.errors) {
      return Object.fromEntries(Object.entries(data.errors).map(([k, v]) => [k.toLowerCase(), Array.isArray(v) ? v.join(' ') : String(v)]));
    }
    return { general: withErrorId(data?.message || data?.title || '', res, data) };
  } catch {
    return { general: withErrorId('', res, null) };
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
