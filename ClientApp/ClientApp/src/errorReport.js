// OB2: errors in the browser go to the server log, so problems on a shop's screen can be found without a phone call.
// Sent: error message, page path (no query string), short stack. Never names, phones or order contents.

const MAX_PER_PAGE = 20
const SAME_ERROR_GAP_MS = 10 * 60 * 1000

let sent = new Map()
let count = 0

export function resetErrorReporting() {
  sent = new Map()
  count = 0
}

export function reportError(message, stack, source) {
  try {
    if (!message) return
    const text = String(message).slice(0, 500)
    const now = Date.now()
    if (count >= MAX_PER_PAGE) return
    const last = sent.get(text)
    if (last !== undefined && now - last < SAME_ERROR_GAP_MS) return
    sent.set(text, now)
    count++
    const body = JSON.stringify({
      message: text,
      stack: stack ? String(stack).slice(0, 4000) : null,
      url: typeof location !== 'undefined' ? location.pathname : '',
      source: source || 'error',
    })
    fetch('/api/client-errors', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body,
      keepalive: true,
      credentials: 'same-origin',
    }).catch(() => {})
  } catch {
    // Error reporting must never break the page.
  }
}

export function installErrorReporting(target = window) {
  target.addEventListener('error', (e) => reportError(e.message || e.error?.message, e.error?.stack, 'error'))
  target.addEventListener('unhandledrejection', (e) => {
    const r = e.reason
    reportError(r?.message || (r === undefined ? '' : String(r)), r?.stack, 'promise')
  })
}
