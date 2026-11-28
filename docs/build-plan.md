# Build plan: AllInOne POS

Written 27 Nov 2026. How the plan in [architecture.md](architecture.md) and the feature list in [supermarket-dmart-style.md](supermarket-dmart-style.md) get built. Phases 1 to 3 are broken into tasks ready to queue; phases 4 to 10 are listed by feature and get broken into tasks just before each phase starts, against the code as it is then.

## How every task is built

- **Small tasks.** One task changes one thing; each Aider step touches one file (two at most). Big files (POSMenu.jsx, 600+ lines) get the exact snippet to paste.
- **Tests first, by Claude.** xUnit for the server, Vitest for screens, Playwright for end-to-end flows. Aider writes the code; a task passes only when the new tests and the whole suite pass.
- **Old tests:** when a feature changes an exact body or text an old test expects, Claude updates that test in the same task.
- **Specs** name the files, the endpoint or component, the rules and the error messages word for word.
- **Migrations:** one per task, written so they also run on shop PCs; data copied before a column is dropped; never two risky migrations in one release.
- **Safety:** a database backup before each release with migrations; an upgrade test on a copy of a real shop database for phases 3 and 4.
- **Release** at the end of each phase (or sooner for fixes). Shop PCs take it 2 days later.
- **Existing clients keep what they have:** every new capability is off for them unless they already use that feature.

## Phase 1: rename to AllInOne POS (about 4 tasks, low risk)

| Task | What | Tests |
|---|---|---|
| RN1 | App name in one i18n key (app.name) used everywhere on screens: title bar, login, about, PWA manifest, index.html title. English, Gujarati, Hindi. | i18n keys match; login and about show "AllInOne POS" |
| RN2 | Printed and sent texts: bill and KOT footers, licence and trial texts, Telegram and email messages, authenticator app issuer name (existing app entries keep working; only the label changes). | bill print text; issuer in the setup URI |
| RN3 | Root README and docs titles. | none (docs) |
| RN4 | Checklist only, done by Mahesh at a quiet time: GitHub repo name, E:\Projects folder, Docker image names, runner script paths, GitHub Actions. | manual |

Do NOT rename in phase 1: database names, OpenTelemetry service name and Loki labels (Grafana alerts depend on them), C# namespaces.

## Phase 2: capability registry (about 12 tasks, low to medium risk)

Today's modules: Billing, Inventory, Loyalty, Menu, Orders, Platform, Reservations, Staff, plus shared parts (shifts, credit notes, QR, tips, recipes, stations, combos, support).

| Task | What | Tests |
|---|---|---|
| C1 | Capability list in code: key, name, description, depends on. First keys: tables, kitchen-screen, tokens, reservations, qr-ordering, feedback, loyalty, stock, recipes, combos, sell-by-weight, simple-counter, shifts, credit-notes, tips, expenses. | every dependency exists; no cycles |
| C2 | Outlet types (templates) owned by the platform: restaurant, cafe, takeaway counter, cloud kitchen, bakery, dairy; each = capabilities + settings preset (moves today's presets out of the owner's settings). | each type only uses known keys; presets give the same settings as today |
| C3 | Store capabilities per restaurant (JSON list column) + migration that gives every existing restaurant everything it uses today. | migration backfill; new restaurant gets its template |
| C4 | Plan carries capabilities: subscription plans list allowed capabilities; the restaurant gets the intersection. | a capability outside the plan cannot be turned on |
| C5 | Shop PC licence lease carries the capabilities, signed; the shop PC enforces them offline. | changed lease fails the signature check; old leases without the list keep today's behaviour |
| C6 | Server check: endpoint filter RequireCapability("x") returning 403 with a clear message. | off gives 403, on gives 200 |
| C7a-e | Apply the filter one module per task: reservations; loyalty; stock and recipes; tables, kitchen screen and tokens; QR ordering and feedback. | per module: off 403, on 200; whole suite green |
| C8 | GET /api/me/capabilities; frontend capability context; menus and routes hidden when off. | Vitest: nav hides off items; direct URL shows "not in your plan" |
| C9 | Platform Add restaurant: choose the outlet type before creating; the feature checklist is pre-filled from the type and each feature can be ticked or unticked (custom). | create with type; create with custom features; owner role cannot create (403) |
| C9b | Platform edit restaurant: change the type or single features later, only the platform, audit logged. Existing restaurants become type custom with exactly the features they use today. | audit row; backfill keeps features; owner cannot change (403) |
| C9c | Owner Settings: no outlet type buttons and no capability switches; shows the outlet type as set by the provider; server refuses owner changes to platform-controlled settings; owner keeps time zone, tokens, receipt, GST. | 403 for controlled settings; operational settings still save |
| C10 | Release and check on staging with a copy of a real restaurant. | Playwright smoke: restaurant flow unchanged |
| C11 | Demo data per outlet type: the platform Demo data button fills data that fits the type (restaurant, cafe, takeaway counter, cloud kitchen, bakery, dairy with items sold by kg or litre); only for empty restaurants. | one test per type; refused when the restaurant already has data |

## Phase 3: modules and database schemas (about 16 tasks, medium risk)

| Task | What | Tests |
|---|---|---|
| M1 | IModule interface: register services, map endpoints, configure its part of the database model, list its capabilities. Program.cs loops over modules. Existing code wrapped, not moved yet. | endpoint list snapshot: every route still mapped with the same roles |
| M2a-h | One module per task: move its database model setup out of the shared AppDbContext partial files into the module (Billing, Inventory, Loyalty, Menu, Orders, Platform, Reservations, Staff). | model snapshot unchanged (no migration generated) |
| M3 | Schema plan: which tables go to which schema (core, restaurant, stock, loyalty, platform...). Written down, reviewed by Mahesh. | none (doc) |
| M4a-e | Move tables into schemas, one group per task (ALTER TABLE SET SCHEMA is quick, no data copied). Update raw SQL (loadtest/check.sql, reports). | upgrade test on a copy of a real shop database; whole suite |
| M5 | Frontend module registry: each module lists its routes and menu entries; loaded only when its capability is on (lazy loading). | Vitest: routes from the registry; bundle split |
| M6 | Release, staging check, then shop PCs. | Playwright smoke |

## Phases 4 to 10 (by feature; tasks written before each phase)

- **Phase 4, generic core (high risk):** MenuItem becomes Item, Order becomes Sale, OrderItem becomes SaleLine; restaurant-only fields (table, token, kitchen station, round) move to restaurant tables. Step by step: new tables, copy, switch reads, switch writes, drop old. Backup and upgrade test each step.
- **Phase 5, stock ledger, barcodes, search, custom fields:** stock as movements; several barcodes per item; pg_trgm name search; JSONB custom fields defined by the platform.
- **Phase 6, grocery R1 and supermarket checkout:** scan-first POS screen, shared product catalog with last known MRP, MRP rule, owner price rules, cashier discount list, PLU codes, shelf labels, cashier shifts with cash count and X/Z reports, supervisor PIN for voids, customer display, e-bill, grocery and supermarket templates, grocery plan. **Big starter catalog**: several thousand products most kirana and DMart-style stores sell (staples, packaged food, snacks, beverages, personal care, home care, baby care, stationery) with barcode, names in English and Gujarati, brand, pack size, unit, category, HSN, GST and last known MRP, built from Open Food Facts India plus a curated list; a new store imports it by category in one click, and the demo data for grocery and supermarket uses it with sample prices.
- **Phase 7, purchase and credit:** purchase orders, goods receipt, supplier bills, stock take with a handheld scanner, wastage write-offs, batch and expiry (oldest expiry first), returns against a bill with credit note, khata.
- **Phase 8, pharmacy:** batch and expiry required, Schedule H register, doctor and patient on the bill, drug licence on the invoice, search by salt, substitutes.
- **Phase 9, offers and many stores:** offers engine (buy X get Y, combos, quantity price, bill-value), barcode label scales, auto replenishment, head office product and price master, central purchasing, warehouse, store transfers.
- **Phase 10, connectors:** event outbox, webhooks, Tally, GSTR-1 export, Swiggy and Zomato, GST e-invoice.

## Order and timing

1. Release v2.3.2.
2. Phase 1, then phase 2, then phase 3, each ending with a release.
3. Before Claude Pro ends (about 10-11 Dec 2026): Claude writes the tests and specs for phases 1 to 3 so they can keep running with Aider.
4. Each later phase starts with Claude breaking it into tasks against the current code.
