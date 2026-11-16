import { describe, it, expect, beforeEach } from 'vitest';
import { saveOffline, listOffline, syncOffline, retryOffline, removeOffline, resetOfflineForTests, isUnreachable, MAX_TRIES } from './orderQueue';

const order = (id, extra = {}) => ({ clientOrderId: id, orderType: 'Takeaway', customerName: 'Asha', items: [{ menuItemId: 1, quantity: 2 }], ...extra });
const reply = (status, body = {}) => ({ ok: status >= 200 && status < 300, status, json: async () => body });

beforeEach(() => resetOfflineForTests());

describe('offline order queue', () => {
  it('sends waiting orders oldest first with their own client ids, then forgets them', async () => {
    await saveOffline(order('a'));
    await new Promise((r) => setTimeout(r, 5));
    await saveOffline(order('b'));
    const seen = [];
    const result = await syncOffline(async (url, options) => {
      const body = JSON.parse(options.body);
      seen.push(body.clientOrderId);
      return reply(200, { orderId: seen.length, tokenNumber: 40 + seen.length });
    });
    expect(seen).toEqual(['a', 'b']);
    expect(result.sent).toEqual([{ clientOrderId: 'a', tokenNumber: 41 }, { clientOrderId: 'b', tokenNumber: 42 }]);
    expect(await listOffline()).toHaveLength(0);
  });

  it('keeps everything when the server is still unreachable', async () => {
    await saveOffline(order('a'));
    await saveOffline(order('b'));
    expect((await syncOffline(async () => { throw new TypeError('Failed to fetch'); })).waiting).toBe(2);
    expect((await syncOffline(async () => reply(502))).waiting).toBe(2);
    expect((await syncOffline(async () => reply(401))).waiting).toBe(2);   // login expired: wait for sign-in, do not drop
    expect(await listOffline()).toHaveLength(2);
  });

  it('marks an order the server refuses, keeps sending the rest, and can retry or remove it', async () => {
    await saveOffline(order('bad'));
    await new Promise((r) => setTimeout(r, 5));
    await saveOffline(order('good'));
    const result = await syncOffline(async (url, options) => (JSON.parse(options.body).clientOrderId === 'bad'
      ? reply(400, { message: 'An item in the order is not on the menu.' })
      : reply(200, { tokenNumber: 7 })));
    expect(result.refused).toEqual([{ clientOrderId: 'bad', message: 'An item in the order is not on the menu.' }]);
    expect(result.sent).toEqual([{ clientOrderId: 'good', tokenNumber: 7 }]);
    const left = await listOffline();
    expect(left).toHaveLength(1);
    expect(left[0]).toMatchObject({ clientOrderId: 'bad', status: 'refused', error: 'An item in the order is not on the menu.' });

    // A refused order is not sent again by itself...
    let calls = 0;
    await syncOffline(async () => { calls++; return reply(200, {}); });
    expect(calls).toBe(0);
    // ...only after the cashier asks for it.
    await retryOffline('bad');
    await syncOffline(async () => { calls++; return reply(200, { tokenNumber: 8 }); });
    expect(calls).toBe(1);
    expect(await listOffline()).toHaveLength(0);

    await saveOffline(order('x'));
    await removeOffline('x');
    expect(await listOffline()).toHaveLength(0);
  });

  it('never sends the same order twice at the same time', async () => {
    await saveOffline(order('a'));
    let calls = 0;
    const slow = async () => { calls++; await new Promise((r) => setTimeout(r, 20)); return reply(200, { tokenNumber: 1 }); };
    await Promise.all([syncOffline(slow), syncOffline(slow), syncOffline(slow)]);
    expect(calls).toBe(1);
  });

  it('keeps an order waiting after a server error and shows it as not accepted only after 5 tries', async () => {
    await saveOffline(order('a'));
    for (let i = 1; i < MAX_TRIES; i++) {
      const result = await syncOffline(async () => reply(500));
      expect(result).toMatchObject({ refused: [], waiting: 1 });
    }
    const last = await syncOffline(async () => reply(500));
    expect(last.refused).toEqual([{ clientOrderId: 'a', message: 'Error 500' }]);
    // "Try again" starts the count again, and a good answer then sends it.
    await retryOffline('a');
    expect((await syncOffline(async () => reply(500))).waiting).toBe(1);
    expect((await syncOffline(async () => reply(200, { tokenNumber: 3 }))).sent).toEqual([{ clientOrderId: 'a', tokenNumber: 3 }]);
  });

  it('treats gateway errors and timeouts as unreachable', () => {
    expect([0, 408, 429, 502, 503, 504].every(isUnreachable)).toBe(true);
    expect([400, 409, 500].some(isUnreachable)).toBe(false);
  });
});
