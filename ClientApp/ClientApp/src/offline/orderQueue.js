// Orders taken while the server cannot be reached wait here (IndexedDB in the browser) and are sent when it is back.
// Sending is safe to repeat: every order carries its clientOrderId, and the server returns the same order for the same id.

const DB_NAME = 'pos-offline';
const STORE = 'orders';
export const CHANGED_EVENT = 'pos-offline-changed';

export const MAX_TRIES = 5; // server errors (5xx) are tried this many times before the order is shown as not accepted

let memory = null; // used when IndexedDB is missing (tests, very old browsers)
let syncing = null;
let channel = null; // tells the other tabs of this till that the waiting list changed

function memoryStore() {
  if (!memory) memory = new Map();
  return {
    all: async () => [...memory.values()],
    put: async (entry) => { memory.set(entry.clientOrderId, entry); },
    remove: async (id) => { memory.delete(id); },
  };
}

function openDb() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE, { keyPath: 'clientOrderId' });
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function run(mode, action) {
  return openDb().then((db) => new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, mode);
    const result = action(tx.objectStore(STORE));
    tx.oncomplete = () => { db.close(); resolve(result?.result); };
    tx.onerror = () => { db.close(); reject(tx.error); };
  }));
}

function storage() {
  if (typeof indexedDB === 'undefined') return memoryStore();
  return {
    all: () => run('readonly', (s) => s.getAll()),
    put: (entry) => run('readwrite', (s) => s.put(entry)),
    remove: (id) => run('readwrite', (s) => s.delete(id)),
  };
}

function tabs() {
  if (channel === null) {
    channel = false;
    if (typeof window !== 'undefined' && typeof BroadcastChannel !== 'undefined') {
      try {
        channel = new BroadcastChannel('pos-offline');
        channel.unref?.();
        channel.onmessage = () => window.dispatchEvent(new CustomEvent(CHANGED_EVENT));
      } catch {
        channel = false;
      }
    }
  }
  return channel;
}

function changed() {
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new CustomEvent(CHANGED_EVENT));
  const other = tabs();
  if (other) other.postMessage('changed');
}

// Only for tests: start with an empty queue.
export function resetOfflineForTests() {
  memory = new Map();
  syncing = null;
}

export async function saveOffline(body) {
  await storage().put({ clientOrderId: body.clientOrderId, body, savedAt: new Date().toISOString(), status: 'waiting', error: null });
  changed();
}

export async function listOffline() {
  if (typeof window !== 'undefined') tabs(); // start listening to the other tabs
  const all = await storage().all();
  return all.sort((a, b) => a.savedAt.localeCompare(b.savedAt));
}

export async function removeOffline(clientOrderId) {
  await storage().remove(clientOrderId);
  changed();
}

// Puts a refused order back in line (after the cashier fixed the cause, e.g. turned an item back on).
export async function retryOffline(clientOrderId) {
  const entry = (await storage().all()).find((e) => e.clientOrderId === clientOrderId);
  if (!entry) return;
  await storage().put({ ...entry, status: 'waiting', error: null, tries: 0 });
  changed();
}

// True when the answer means "the server is not reachable right now", so the order should wait and try again.
export function isUnreachable(status) {
  return status === 0 || status === 502 || status === 503 || status === 504 || status === 408 || status === 429;
}

// Sends waiting orders oldest first. Stops at the first sign the server is still unreachable or the login has expired.
// A server error (5xx) leaves the order waiting for the next round; after MAX_TRIES it is shown as not accepted.
async function sendWaiting(fetchImpl) {
  const sent = [];
  const refused = [];
  const store = storage();
  for (const entry of await listOffline()) {
    if (entry.status !== 'waiting') continue;
    let res;
    try {
      res = await fetchImpl('/api/orders', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(entry.body) });
    } catch {
      break; // still offline
    }
    if (isUnreachable(res.status) || res.status === 401) break;
    const data = await res.json().catch(() => ({}));
    if (res.ok) {
      await store.remove(entry.clientOrderId);
      sent.push({ clientOrderId: entry.clientOrderId, tokenNumber: data.tokenNumber });
    } else if (res.status >= 500 && (entry.tries ?? 0) + 1 < MAX_TRIES) {
      await store.put({ ...entry, tries: (entry.tries ?? 0) + 1 });
    } else {
      const message = data.message || `Error ${res.status}`;
      await store.put({ ...entry, status: 'refused', error: message });
      refused.push({ clientOrderId: entry.clientOrderId, message });
    }
  }
  const waiting = (await listOffline()).filter((e) => e.status === 'waiting').length;
  if (sent.length || refused.length) changed();
  return { sent, refused, waiting };
}

// Returns { sent: [{ clientOrderId, tokenNumber }], refused: [{ clientOrderId, message }], waiting }.
// Only one tab of this till sends at a time (browser lock), so two open tabs never send the same order together.
export function syncOffline(fetchImpl = (...args) => fetch(...args)) {
  if (!syncing) {
    const locks = typeof navigator !== 'undefined' ? navigator.locks : null;
    const busyElsewhere = async () => ({ sent: [], refused: [], waiting: (await listOffline()).filter((e) => e.status === 'waiting').length });
    const work = locks
      ? locks.request('pos-offline-sync', { ifAvailable: true }, (lock) => (lock ? sendWaiting(fetchImpl) : busyElsewhere()))
      : sendWaiting(fetchImpl);
    syncing = Promise.resolve(work).finally(() => { syncing = null; });
  }
  return syncing;
}
