# Roadmap, Decisions and Workflow (current)

## v1.7 design: hybrid edge, like Petpooja Offline (15 Nov 2026)

- Shop mini PC runs the same Docker images (POS, kitchen, database) and bills without internet.
- One-way sync, cloud to shop only: menu changes, settings, licence renewal, updates, later online orders. The shop sends only a heartbeat; bills never leave the shop unless the owner opts in to share daily totals.
- Signed licence works 7 days offline.
- Many outlets: one head-office server reached over a private link (e.g. Tailscale), or one PC per outlet with opt-in combined reports.
- Nightly USB/NAS backup, optional encrypted cloud copy with an owner-held key; recommended mini-PC list and one-command installer.
- Code to add: edge mode with first-run setup; sync agent (polls the cloud, applies each change exactly once); licence lease; updater with rollback; HTTPS on the local network.
- Competitor: Petpooja (Ahmedabad, about 1.5 lakh restaurants). Our edge: price shown openly, full Gujarati, local personal support, 15-minute setup (menu from a photo).

---

## Roadmap v4: complete package before pitching (14 Nov 2026)

Replaces the "pilot minimum" order below. No friendly restaurant for a pilot, so the full package comes first, then pitch to Anand restaurants with a free trial.

**Done (14 Nov 2026):** v1.4 pilot minimum; v1.5 go-live: platform owner panel (/platform), hardening (rate limits, health checks, Telegram error alerts, uptime check), production stack (Caddy HTTPS on [sslip.io](http://sslip.io), PostgreSQL 17, release on tag via GitHub Packages), nightly backups with weekly restore test, Lightsail server in Sydney via OpenTofu (state in CloudShell, S3 skipped for now). Live: [https://13-236-102-22.sslip.io](https://13-236-102-22.sslip.io)

- **v1.5 close-out (now):** menu scan (photos to Gemini to review table, CSV fallback); fix server setup script (Lightsail runs it with sh); staging later (rc tags to staging, final tags reuse tested images)
- **v1.6 Billing and receipts:** gapless invoice numbers per outlet and financial year; regular GST (CGST/SGST), composition and no-GST bills; 58/80 mm thermal bill and KOT print; cash, UPI QR with exact amount, card; split bill, discounts; void and reprint with reason and manager PIN; credit notes; day close with cash count; bill in English and Gujarati; receipt settings (GSTIN, FSSAI, footer)
- **v1.7 Offline and on-premise edition:** PWA offline queue (IndexedDB); local mini-PC server with the same Docker images; first-run setup (single restaurant); HTTPS on the local network; updater with rollback; backups to USB/NAS with age warning; signed licence file (30-day trial, then yearly; tied to the PC; 15-day warning, 7-day grace; clock-rollback check; after expiry no new orders, bills stay viewable/exportable); optional owner-approved remote support; encrypted cloud backup with owner-held key as a middle option
- **v1.8 Restaurant essentials:** variants and add-ons; kitchen stations with KOT per station; running tab; table move/merge/split; table status; token display screen; staff PINs and permissions; time-based menu; combos and promotions
- **v1.9 Reports:** about eight clear reports; GST summary for the accountant; item/category/payment/hourly/staff; Excel and PDF export; daily summary by WhatsApp or e-mail; expenses at day close
- **v2.0 Pitch-ready:** demo restaurant with reset; setup wizard (scan menu, tables, staff, printer test); help pages in Gujarati and English; terms and privacy; pricing; brochure and demo video; cloud self-signup and subscriptions
- **Then:** pitch with free trial. Later: inventory, QR/online ordering, delivery apps, loyalty
- **Advice noted:** from v1.6, use the live cloud demo for short talks with 3-5 Anand owners to reorder v1.7-v1.9 by what they ask for

---

<aside>
📌

**Current plan as of 13 Nov 2026.** This page replaces the stacked roadmap blocks on the Project Hub. Repo: `srkneo/RestaurantPOS` (main branch `master`), local path `C:\Projects\RestaurantPOS`.

</aside>

## 1. Status

| Release | Status | Delivered |
| --- | --- | --- |
| v1.0 | Tagged | POS, live kitchen display (SignalR), Mark Ready, POS banner |
| v1.1 | Tagged | Status enum with rules, quantities, EF migrations, tenant and audit columns, tables API, DTOs, validation, error handler, module folders |
| v1.2 | Tagged | Health check, Docker Compose, Playwright E2E in CI, Dependabot auto-merge |
| v1.3 | Built, not tagged | Cookie login, 4 roles, tenant isolation (data, API, SignalR), login page, protected routes, 16 tests. CI and E2E green |

## 2. Current plan: pilot minimum

First market: restaurants in Anand, Gujarat (cafes, takeaway counters, snack shops near the student areas). Goal: reach one pilot restaurant as early as possible, then let its feedback order the rest.

### Before the pilot

1. **v1.3 close-out**
2. **Runner v2**
3. **v1.4 pilot minimum**
    - **Batch 0 Quality base:** real-Postgres tests, lint and format, frontend tests, security scans
    - **Batch U UI base:** Bootstrap theme, shared components, restyled screens, English and Gujarati text files
    - **Batch A Staff:** change password, create and disable users, reset passwords, long-lived kitchen-screen login
    - **Batch B core:** `Tenants` and `Outlets` tables and the other schema fixes, categories, item description, availability, tax rate and veg flag, order type with table or customer name, a note on each order line (for example Jain, no onion or garlic), restaurant timezone and business date, atomic token counter, quantity limit setting, cancel or edit before cooking, order version number (two cashiers cannot overwrite each other), screen version check, `/admin/menu`
    - **Batch C core:** tables CRUD, store profile (name, address, GSTIN, FSSAI, receipt text, tax mode), logo upload, outlet settings page with the takeaway-counter and cafe presets first, audit log recorded (its screen comes later)
4. **v1.5 go-live core:** domain, AWS account, budget alert, OpenTofu for Lightsail, HTTPS, deploy on a version tag, nightly backups with a restore test, secure cookies, login rate limit, uptime alert, error tracking
5. **v1.6 billing core:** invoice numbers per financial year, three bill modes (regular GST, composition, no GST), compliant bill in English and Gujarati, thermal printing tested on a real printer, cash and UPI (QR with the exact amount), void with reason, simple day-close report
6. **Pilot in one Anand restaurant**

### After the pilot (order set by feedback)

- Variants and add-ons, kitchen stations, KOT per station, running tab, table status
- Menu scan, Excel import, demo restaurant with reset, token display screen ("Now serving")
- Audit-log screen, split and merge bills, discounts, tips, card payments, credit notes, shift cash reconciliation
- Platform panel (super admin), two-step login, support access, feature switches
- Email service, daily sales email to owners, one-click data export
- Staging with rollback, k6 load test, automatic release notes, Hindi
- From the competitor review (RestosoftIN): time-based menu, expense tracking at day close, customer feedback through a QR code on the bill, finer staff permissions (void, discount, reprint), promotions and combos
- Reports: about eight clear daily ones, not dozens. Bulk SMS not planned
- Then v1.7 Offline, v1.8 Reports, v1.9 Inventory, v2.0 Online orders, v2.1 Sell it

### Positioning and sales material

- Price shown on the website, free trial without a sales call, setup in minutes
- Gujarati, local in Anand, same-day visits
- Works in any browser, live kitchen screen included
- Honest, specific claims only
- WhatsApp demo button, one-page brochure in Gujarati and English, testimonials with restaurant name and town, list of outlet types served

### Rules added

- **Review gate:** changes to login and roles, restaurant separation, and billing maths are read by a person (or by Claude from the real diff) before merging. Everything else stays automatic
- **Records:** invoices are never deleted (confirm the GST record-keeping period with a CA). A restaurant that leaves gets an export, then deletion after a set period
- **Support:** stated support hours in the terms, a runbook someone trusted could follow
- Not needed for Anand: bar and liquor features. Delivery-app integration stays late

## 3. Full backlog by release

### v1.3 close-out

1. Own login test: `docker start pos-postgres`, then `dotnet run --launch-profile https` with `Admin__Email` and a new `Admin__Password` (upper, lower, digit, symbol)
2. Merge `feature/v1.3` into `master`, tag `v1.3.0`
3. GitHub settings: allow auto-merge, require `build-test` and `e2e` on `master`
4. Dependabot PR #11: re-test on new `master`. If EF Core is bumped, update `dotnet-ef` to the same version

### Runner v2

- `expect:` lines: text that must exist in a file after a task, else the task fails
- `needs:` lines: skip a task when a task it depends on failed
- Lock file: a second runner refuses to start
- Long new files are written by the runner, not the model. Empty files count as failures
- Build and test output saved for every failed attempt
- Full check at the end, summary file, auto push
- Telegram notification with the summary (env vars `POS_TG_TOKEN`, `POS_TG_CHAT`; bot made with BotFather; token never committed or pasted in chat)
- Task queue folder: several task files run in order from one start

### v1.4 Store setup (full scope; section 2 says what is before the pilot)

- **Batch 0 Quality base:** Testcontainers, lint and format in CI, Vitest, CodeQL and secret scanning, migration upgrade test, OpenAPI snapshot, one Playwright flow per feature plus a two-restaurant isolation test
- **Batch U UI base:** Bootstrap 5 with `react-bootstrap`; CoreUI free React admin template for the admin area only (check its licence file first); one theme file; shared layout, side menu, buttons, form fields, toasts, confirm dialog, loading, empty and error states; restyle Login, POS, Kitchen, Order History; translation files; Playwright checks at phone, tablet and desktop sizes
- **Batch A Staff:** change own password; admin creates, lists, disables users and resets passwords; long-lived device login for kitchen screens; `/admin/staff`
- **Batch B Menu and orders:** schema fixes; categories table; item description, image, availability, sort order, tax rate; variants and add-ons; kitchen stations; order type; table optional with customer name and phone for takeaway; token settings; running tab; timezone and business date; atomic token counter; quantity limit; table status; cancel or edit before cooking; `/admin/menu`
- **Batch C Store profile:** tables CRUD; restaurant settings; logo and image upload; audit log; outlet settings page and presets
- **Batch D Menu scan:** photos of a paper menu, vision AI extracts items into an editable review table, saved only after the owner confirms, CSV import on the same screen

### v1.5 Go live (low cost)

1. Domain, AWS account, budget alert
2. OpenTofu: S3 state, GitHub OIDC, plan on pull request, apply on approval
3. Lightsail server (Mumbai), static IP, firewall, bucket, DNS from code
4. Multi-arch Docker images, deploy from GitHub on a version tag
5. Staging copy with automatic rollback on failed health check
6. Secrets only on the server
7. Uploads and nightly backups in the bucket, monthly restore test
8. Email service (SES), error tracking, uptime and phone alerts
9. Secure cookies, proxy headers, login rate limits, compression, output and in-memory caching, ETags, CDN
10. Platform-owner panel, feature switches per restaurant
11. Automatic release notes, runbook, k6 load test

### v1.6 Billing

1. Invoice numbers per financial year (atomic counter)
2. Tax engine: inclusive or exclusive, CGST and SGST as separate lines, composition bill of supply
3. Compliant bill: GSTIN, FSSAI number, SAC 9963, logo, no automatic service charge
4. Printing tested with a real thermal printer (print helper if needed)
5. Payments: cash, UPI, card, split and merge, discounts, tips
6. Voids and credit notes in the audit log; issued bills never edited
7. KOT printing per station
8. Shift and day close with cash reconciliation
9. Hindi and Gujarati on kitchen screen and bills, owner help pages

### Later releases

- **v1.7 Offline:** PWA, order queue in IndexedDB, batch sync, per-device tokens, sync indicator
- **v1.8 Reports:** sales by day, item, category, payment mode, staff; GST summary; CSV and Tally-friendly export; dashboard; daily email
- **v1.9 Inventory:** ingredients, stock, purchases, recipes deducted per sale, low-stock alerts
- **v2.0 Online orders:** QR table ordering, then Swiggy, Zomato and ONDC through a partner
- **v2.1 Sell it:** signup and onboarding, subscriptions, loyalty with DPDP consent, support

### Scaling (only when signals appear)

1. Bigger server
2. Managed Postgres
3. Several app servers behind a load balancer, Redis
4. Split restaurants across stacks
5. Extract single modules as microservices

## 4. Architecture decisions

- Modular monolith: one [ASP.NET](http://ASP.NET) Core API, one React app, modules in `Modules/Orders`, `Modules/Menu`, `Shared`
- Multi-tenant: one server and one database serve all restaurants; `TenantId` and `OutletId` on every row
- PostgreSQL only (`jsonb` for flexible data). No NoSQL except the browser's IndexedDB for the offline queue
- Hosting: Lightsail first, EC2 Graviton later. OpenTofu for infrastructure. About $15-30 a month to start
- Caching: browser and CDN, output and in-memory cache, ETags, PWA cache. Redis only with 2+ servers

## 5. Database plan

**Problems to fix in v1.4 Batch B**

1. No `Tenants` or `Outlets` tables behind TenantId
2. Deleting a table cascades to its orders: change to restrict, use soft delete
3. Token numbers not unique and use the UTC day: add `BusinessDate`, `Counters`, unique index
4. `OrderItems` has no TenantId and no foreign key to `MenuItems`
5. Category is free text
6. Missing indexes (tenant, outlet, status, created time)
7. `CreatedBy` and `UpdatedBy` never filled
8. No status timestamps
9. Unused `KitchenOrderItems` table in the local database

**Target tables**

| Module | Tables |
| --- | --- |
| Platform | `Tenants`, `Outlets`, `FeatureFlags` |
| Identity | Users (IsActive, IsDevice, IsPlatformAdmin), roles |
| Menu | `Categories`, `MenuItems`, `ItemVariants`, `AddOns`, `KitchenStations` |
| Orders | `RestaurantTables`, `KitchenOrders`, `OrderItems`, `Counters` |
| Billing (v1.6) | `Invoices`, `InvoiceLines`, `Payments`, `CreditNotes`, `Shifts` |
| Operations | `AuditLogs`, `OutboxEvents`, `MenuImports` |
| Later | Devices and sync, inventory tables, `Customers`, `Subscriptions` |

**Rules:** TenantId first in every index. Money as `numeric(12,2)`. Times in UTC plus a local business date. Soft delete for anything a bill can reference. Foreign keys restrict, not cascade. Bills copy names, prices and tax at issue time.

## 6. How development works

| Who | Does |
| --- | --- |
| Claude | Plans releases, writes small task files, reads logs and code, writes fix tasks. Never edits files on the PC |
| Local LLM (Aider + qwen2.5-coder:14b, RTX 4070 Ti 12 GB) | Small edits, pattern-following tests |
| Runner (`C:\Projects\runner.ps1`, tasks in `C:\Projects\tasks`) | Runs tasks one by one, builds and tests, commits or reverts and retries, pushes |
| GitHub Actions | CI and E2E on every push, Dependabot updates |
| Mahesh | Starts runs, sends the summary, does a short manual test, merges and tags |

**Cycle per batch:** task file, setup block, start runner, summary, fix file if needed, CI and E2E, manual check, merge and tag.

**Task-file rules:** every task leaves build and tests green on its own; a task that changes behaviour includes its test file; long new files use "Create this file with exactly this content:"; one small edit per task with exact anchor lines; no open-ended prompts; no URLs or shell commands in prompts; never two runners at once.

## 7. UI plan

- Approved look: clean and flat, rounded corners, touch targets 48px or more
- Accent green `#0F7B5A`; Inter for text, Space Grotesk for headings and numbers
- POS: light theme, order panel always visible, toasts instead of pop-ups
- Kitchen: dark theme, very large tokens, tickets labelled New, Waiting, Late with minutes
- Checklist for every screen: works on phone, tablet and desktop; loading, empty and error states; form errors next to the field; confirm before delete or void; common action in one or two taps

## 8. How login works

1. Login page posts email and password to `/api/auth/login`
2. Backend checks the hashed password and sets a signed cookie holding user, role and restaurant
3. The browser sends the cookie with every request and the SignalR connection
4. The app calls `/api/auth/me` and shows only the pages the role may use
5. The server enforces roles and filters all data by restaurant

Roles: cashier (POS, history; places orders), kitchen (kitchen screen; changes status), manager (both), admin (everything). Session lasts 12 hours. Five wrong passwords lock the account for a few minutes.

## 9. Configurable outlets

**Settings per outlet** (stored as `jsonb` on `Outlets`, enforced by the server, set by the owner in Admin > Settings)

| Setting | Options |
| --- | --- |
| Order types offered | Dine-in, takeaway, delivery (any mix) |
| Default order type | The most common one |
| Tables | On or off |
| Customer name | Off, optional or required, per order type |
| Customer phone | Off, optional or required |
| Payment timing (v1.6) | Pay first or pay after the meal |
| Kitchen screen, kitchen stations | Each on or off |
| Token numbers | Start number, daily reset, prefix |
| Running tab | On or off |
| Tax mode (v1.6) | Regular GST, composition, no GST |
| Receipt (v1.6) | Header, footer, logo, language |
| Service charge, tips, discounts | Each on or off |

**Presets** (picked at setup, every setting changeable later)

| Preset | Switches on |
| --- | --- |
| Dine-in restaurant | Tables, pay after, running tab, kitchen screen, stations |
| Quick service or takeaway counter | No tables, customer name, pay first, token display |
| Cafe | Tables optional, both order types, pay first |
| Cloud kitchen | Delivery only, name and phone required, no tables |
| Food stall or canteen | Tap items, pay, token. No kitchen screen |

Each preset has a Playwright test. Customer phone is personal data: optional by default, with consent and deletion handling before wide use.

## 10. Two admin levels

|  | Super admin (Mahesh) | Restaurant admin (owner) |
| --- | --- | --- |
| Sees | All restaurants | Only their own restaurant |
| Panel | `/platform` | `/admin` |
| Restaurants | Create, disable, delete | None |
| Subscription and plan | Set plan, trial, payment status | View their own plan |
| Feature switches | On or off per restaurant | None |
| Staff | First owner account, owner password reset | Manage their own staff |
| Menu, tables, settings | Only through support access | Full control |
| Health and usage | Every restaurant | Their own reports |
- Super admin account has the `IsPlatformAdmin` flag and belongs to no restaurant. Only `/platform` endpoints skip the restaurant filter
- Support access: a time-limited "view as this restaurant" mode with a banner and an audit-log entry
- Super admin protection: two-step login, phone alert on every login, not used day to day

## 11. Getting customers

1. One free pilot restaurant in Anand (after v1.6) in exchange for feedback and a testimonial
2. Target small outlets: cafes, QSRs, snack shops, cloud kitchens, canteens
3. Reasons to switch: lower price with no setup fee, quick setup, no special hardware, local WhatsApp support in Gujarati
4. Channels: walk-ins at 3-5 pm with a demo, referrals, partners (printer dealers, CAs, suppliers), WhatsApp demo video, one-page website
5. Free trial, done-for-you setup, monthly billing, data export

## 12. Compliance notes (confirm with a CA or lawyer)

- GST bill: CGST and SGST shown separately, unique serial per financial year, composition restaurants issue a bill of supply
- FSSAI 14-digit number on every bill
- E-invoicing only above ₹5 crore turnover, not for B2C bills
- DPDP Rules 2025 apply once customer personal data is stored

## 13. Open items

- Free about 17 GB on C: (move Ollama models and Docker disk) so Claude can read project files
- Telegram bot for run notifications
- Business items: domain, AWS account, terms and privacy policy, subscription invoicing, payment gateway KYC, support channel, pricing, CA questions, printer choice, menu photos

## Update 14 Nov 2026

### v1.3 closed

- Own login test passed (sign in, POS, kitchen, order history).
- feature/v1.3 merged into master (merge commit b8976c7). The conflict in RestaurantPOS.csproj came from Dependabot raising packages to 10.0.15; it was resolved with all three packages on 10.0.15. The empty Shared/AppUser.cs was removed. 16 tests pass.
- Tag v1.3.0 moved to the merge commit; it had first been pushed by mistake on the old master.
- dotnet-ef updated to 10.0.15 so it matches the EF packages.
- Branch protection rule on master with required checks build-test and e2e.
- RestaurantPOS.slnx was empty since 6 Nov; it now lists RestaurantPOS.csproj and RestaurantPOS.Tests, so Visual Studio loads both projects.
- Project folder now at E:\Projects (moved from C:).

### Runner v2 (E:\Projects\[runner2.ps](http://runner2.ps)1; old [runner.ps](http://runner.ps)1 kept)

- Run summary sent by Telegram bot (env vars POS_TG_TOKEN and POS_TG_CHAT, set with setx; test message works). The token is never pasted in chat or committed.
- Lock file: only one runner at a time. Refuses to start if Ollama is down or the RestaurantPOS app is running (it locks the build).
- Exact done-file match (no more substring matches).
- New task lines: expect: path => text, absent: path => text, needs: other-task, setup: command (for example a package install), allow: extra paths. @@file ... @@end blocks are written word for word by the runner, so a test file goes in the same task as the code.
- A task fails if Aider changes any file it was not allowed to touch.
- Full check at the end (build, tests, npm build); push only if it passes. Summary in %USERPROFILE%\runner-summary.txt.
- Queue mode: -Queue E:\Projects\tasks\queue runs task files in name order, moves finished ones to done, stops at the first failure.
- Fixed from v1: log lines could leak into function return values; Aider flags --no-suggest-shell-commands, --no-detect-urls and --no-auto-lint added.

### Roadmap additions

- Swagger: v1.4 Batch 0 gets an API explorer page in development only (Scalar on the existing OpenAPI document, AllowAnonymous in development, never in production), plus endpoint names, summaries and typed results.
- Offline licence (v1.7): signed 7-day lease renewed whenever the device is online, no new orders after expiry, clock-rollback check. Terminated restaurants get no login, sync or renewal, so devices stop within 7 days. Same lease for the v2.0 edge server.
- Account stages (v2.1): active, payment overdue (warning banner), suspended (read-only, export allowed), terminated (export window such as 30 days, then deletion). Never wipe unsynced orders; GST invoices stay exportable; terms state notice period, export window and deletion.

### Now running

- First v1.4 run: tasks\v1.4-0a.txt on branch feature/v1.4-0. Task 0a adds the Scalar API explorer at /scalar (development only) with 3 tests; task 0b removes the weatherforecast sample code.

## Status and roadmap update (15 Nov 2026)

### Done in v1.6 (merged)

- Gapless invoice numbers, GST modes (none / regular CGST+SGST / composition), round-off
- Payments: cash, UPI QR, card, part payments, discounts, void with reason
- Bill and KOT print (58/80 mm, English/Gujarati), billing settings page
- Till shifts with cash count, day (Z) report and print, print-tab fix (PR #40)

### Order of work now

1. F7a: manager PIN approvals (void, reprint), credit notes for earlier-day bills, credit notes in the day report (task file ready)
2. F7b: screens for F7a (PIN on Staff page, PIN box, credit note button, credit note print, Z report section), then release v1.6.x
3. Batch Q: data safety and load testing
4. v1.7 offline / on-premise edition

### Batch Q: data safety and load testing

- Correctness tests on real Postgres: 2 restaurants (no data crossing), 2 outlets of one restaurant (separate invoice numbers), 2 tills billing at the same second (no duplicate or skipped numbers, no lost payments), menu price change during billing (old orders keep old price), business-day change at midnight
- Server always prices from the menu; fake prices from the screen are ignored
- Random-bill tests of the bill maths: lines add up, CGST = SGST, round-off under ₹1, nothing negative
- k6 load test before each release: about 50 restaurants × 5 tills for 30 minutes with lunch-rush bursts, big menus and big orders, on a test server the size of production; a checker then re-adds every bill and reports mismatches, number gaps or cross-restaurant rows
- One-time capacity test: how many restaurants one $12 server carries
- Nightly production check: re-add the day's bills, Telegram alert on any mismatch

### Tips per table (v1.8)

- Waiter assigned to each table or order
- Tip recorded at payment, separate from the bill (not in GST)
- Goes to that waiter, or an equal pool for the shift if the owner chooses
- Cash tips counted in the till shift; tips per waiter in reports and the Z report

### Swiggy / Zomato

- Moved to later, once there are real customers
- Neither has an open API; go through a middleware partner such as UrbanPiper first (about ₹3,000–6,000/month, usually paid by the restaurant as an add-on), direct partnership later

## Update 17 Nov 2026: licence done, feasible features and QR table ordering added

**Done:** v1.7 G2 shop-PC installer and signed licence (core, shop side, cloud side, screens) merged; installer tested in WSL Ubuntu with Docker Desktop. Next: installer licence step (2e), commit the public key, private key on the production server, tag v1.7.0.

**Order from here**

1. v1.7 rest: updater with rollback, USB/NAS backups with age warning, cloud-to-shop one-way sync, power-off tests, recommended mini-PC list.
2. v1.8 essentials: variants and add-ons, kitchen stations, running tab, table status/transfer/merge/split, token display screen, staff permissions, tips per table, time-based menu, combos, captain/waiter mode.
3. v1.8b QR table ordering (below).
4. v1.9: reports plus Tally export.
5. v2.0 pitch-ready: plus WhatsApp bill link, Google review link, feedback QR on the bill.
6. Later: loyalty/CRM, inventory, reservations, Swiggy/Zomato via middleware partner, Windows installer.

**QR table ordering (v1.8b)**

- Each table has its own QR code holding a long random token that maps to that table, so the table is detected automatically and cannot be changed or guessed.
- Guest sees the menu on their phone, adds items and orders; the order goes to the kitchen screen with the table number and is added to the table's running tab. No waiter needed.
- Owner setting: send straight to the kitchen, or wait for a staff tap.
- Call waiter and Request bill buttons; printable sheet of table QR codes; view-only menu option.
- Safety: rate limits per table, regenerate a table's token if misused.
- Shop PC: guest phone must be on the restaurant Wi-Fi and the PC needs a fixed local address. Cloud version works over mobile data.
- Depends on running tab and table status from v1.8.
