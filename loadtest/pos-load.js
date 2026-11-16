// k6 load test: many restaurants billing at once (never run against production).
// Run:  k6 run -e BASE_URL=https://<staging>.sslip.io -e PLATFORM_EMAIL=... -e PLATFORM_PASSWORD=... loadtest/pos-load.js
// Options (-e): RESTAURANTS (50), TILLS (5 per restaurant), SCALE (1 = 30 minutes; 0.2 = 6-minute trial run).
import http from 'k6/http';
import { check, sleep, fail } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL;
const RESTAURANTS = Number(__ENV.RESTAURANTS || 50);
const TILLS = Number(__ENV.TILLS || 5);
const SCALE = Number(__ENV.SCALE || 1);
const RUN = __ENV.RUN_ID || String(Date.now()).slice(-6);
const PASSWORD = 'Load#Test2026';
const JSON_HEADERS = { headers: { 'Content-Type': 'application/json' } };
const m = (minutes) => `${Math.max(1, Math.round(minutes * 60 * SCALE))}s`;

const wrongTotal = new Counter('wrong_bill_total');     // must stay 0
const failedSale = new Counter('failed_sale');
const saleTime = new Trend('sale_duration', true);       // order + bill + payment

const tills = RESTAURANTS * TILLS;
export const options = {
  setupTimeout: '20m',
  noCookiesReset: true,   // a till stays signed in between sales, like a real POS screen
  scenarios: {
    tills: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: m(2), target: tills },       // tills come online
        { duration: m(8), target: tills },       // normal service
        { duration: m(2), target: tills * 2 },   // lunch rush: twice the load
        { duration: m(6), target: tills * 2 },
        { duration: m(2), target: tills },
        { duration: m(10), target: tills },
      ],
      gracefulRampDown: '30s',
    },
  },
  thresholds: {
    wrong_bill_total: ['count<1'],
    failed_sale: ['count<1'],
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<800'],
    sale_duration: ['p(95)<2000'],
  },
};

function post(url, body, tag) {
  return http.post(`${BASE}${url}`, JSON.stringify(body), { ...JSON_HEADERS, tags: { name: tag } });
}

function login(email, password) {
  const res = post('/api/auth/login', { email, password }, 'login');
  if (res.status !== 200) fail(`login ${email}: ${res.status} ${res.body}`);
}

// Creates the restaurants with their menus and tables. Every 10th restaurant gets a big menu (300 items).
export function setup() {
  if (!BASE || !__ENV.PLATFORM_EMAIL || !__ENV.PLATFORM_PASSWORD) fail('Set BASE_URL, PLATFORM_EMAIL and PLATFORM_PASSWORD');
  const shops = [];
  for (let i = 1; i <= RESTAURANTS; i++) {
    login(__ENV.PLATFORM_EMAIL, __ENV.PLATFORM_PASSWORD);
    const email = `load-${RUN}-${i}@test.local`;
    const created = post('/api/platform/tenants', {
      restaurantName: `Load ${RUN} #${i}`, outletName: 'Main', ownerName: `Owner ${i}`, ownerEmail: email, ownerPassword: PASSWORD,
    }, 'setup');
    if (created.status !== 201) fail(`create restaurant ${i}: ${created.status} ${created.body}`);

    login(email, PASSWORD);
    const category = post('/api/admin/menu/categories', { name: 'Mains', sortOrder: 1 }, 'setup').json();
    const count = i % 10 === 0 ? 300 : 12;
    const items = [];
    for (let k = 1; k <= count; k++) {
      // Whole and half-rupee prices, so the round-off to whole rupees is exercised.
      const price = 20 + ((i * 7 + k * 13) % 380) + (k % 3 === 0 ? 0.5 : 0);
      const item = post('/api/admin/menu/items', {
        name: `Item ${k}`, price, categoryId: category.id, description: null, isVeg: true, isAvailable: true, taxRate: 5, sortOrder: k,
      }, 'setup');
      if (item.status !== 201) fail(`item ${i}/${k}: ${item.status} ${item.body}`);
      items.push({ id: item.json().id, price });
    }
    const tables = [];
    for (let k = 1; k <= TILLS * 2; k++) {
      const table = post('/api/admin/tables', { tableNumber: `T${k}`, isActive: true, sortOrder: k }, 'setup');
      if (table.status !== 201) fail(`table ${i}/${k}: ${table.status} ${table.body}`);
      tables.push(table.json().id);
    }
    shops.push({ email, items, tables });
  }
  return { shops };
}

let signedIn = false;
let shown = 0;

// A failed step: show why (first 3 per till), then pause so a broken step does not hammer the server.
function bad(res, step) {
  failedSale.add(1);
  if (shown++ < 3) console.error(`${step} failed: ${res.status} ${String(res.body).slice(0, 300)}`);
  if (res.status === 401) signedIn = false;
  sleep(1);
}

function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = Math.floor(Math.random() * 16);
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

export default function (data) {
  const shop = data.shops[(__VU - 1) % data.shops.length];
  if (!signedIn) {
    login(shop.email, PASSWORD);
    signedIn = true;
  }

  // 1 to 6 different items, sometimes a big party order of 25 lines.
  const lines = Math.random() < 0.05 ? Math.min(25, shop.items.length) : 1 + Math.floor(Math.random() * 6);
  const picked = [...shop.items].sort(() => Math.random() - 0.5).slice(0, lines);
  const items = picked.map((it) => ({ menuItemId: it.id, quantity: 1 + Math.floor(Math.random() * 5) }));
  const expected = Math.round(picked.reduce((sum, it, n) => sum + it.price * items[n].quantity, 0));
  const table = shop.tables[Math.floor(Math.random() * shop.tables.length)];

  const started = Date.now();
  const order = post('/api/orders', { table, items, clientOrderId: uuid() }, 'order');
  if (!check(order, { 'order placed': (r) => r.status === 200 })) { bad(order, 'order'); return; }
  const bill = post(`/api/orders/${order.json().orderId}/bill`, {}, 'bill');
  if (!check(bill, { 'bill made': (r) => r.status === 201 })) { bad(bill, 'bill'); return; }
  const b = bill.json();
  if (!check(b, { 'bill total matches menu': (x) => Number(x.total) === expected })) {
    wrongTotal.add(1);
    console.error(`WRONG TOTAL ${b.invoiceNumber}: got ${b.total}, expected ${expected}`);
  }
  const pay = post(`/api/bills/${b.id}/payments`, { method: 'Cash', amount: b.total, version: b.version }, 'payment');
  if (!check(pay, { 'payment saved': (r) => r.status === 200 })) { bad(pay, 'payment'); return; }
  saleTime.add(Date.now() - started);

  sleep(2 + Math.random() * 4);   // the cashier talks to the next customer
}
