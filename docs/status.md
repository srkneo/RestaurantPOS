# Roadmap status (the hourly check works through this list in order)

Status: todo / queued / done / parked / STOP (wait for Mahesh's "go" in chat before starting) / RELEASE (automatic: when every task above it is done, write E:\Projects\tasks\queue\release.req containing the version; the PC releases only if the E2E tests on GitHub passed for the current master and outside rush hours; then mark it done and message Mahesh the version and what is in it).
Details of each task: docs/build-plan.md in the repo. Task files are named v2.4-<ID>-<short-name>.txt (alphabetical order = run order).
Rules: one new task per hourly run, only when tasks/queue has no task files and nothing failed. Mark a task done when its PR merged (runner-summary.txt shows "merged").

## In progress
- SUP1 support login (cloud): done (SUP1f, PR #249)
- SUP2 shop PC password of the day: done (SUP2b, PR #252)

## Release
- RELEASE: v2.4.2 (UI1 activity log, RN5 business wording, C1-C3; asked by Mahesh 28 Nov 18:00): done (release.req written by Claude in chat)
- RELEASE: v2.3.3 (SUP1, SUP2): done (tagged 28 Nov 02:43 IST)

## Fixes first
- FIX2 audit Details must be JSON (production 500 on support login, 28 Nov 10:44): SupportAccess.cs and SupportDayLogin.cs wrote plain text into AuditLogs.Details, a json column in PostgreSQL; in-memory tests did not catch it. Hotfix queued by Claude as v2.4-0FIX2-audit-json.txt: done (PR #258)
- RELEASE: v2.4.1 (FIX2 hotfix): done (tagged 28 Nov 12:48 IST)
- FIX1 flaky test DayCloseTests.Shift_CountsCashSalesAndMovements_AndShowsShortOnClose: failed in CI at 04:00 IST (22:30 UTC), exactly the business-day start hour (4 AM), so the shift and its sales fell into different business days. Make the test independent of the time of day (fixed clock / set the business day explicitly / create the data relative to the business-day start). Test-only change; also scan the other DayClose and shift tests for the same pattern: done (FIX1, PR #254)

- UI1 Activity log page: there is an API (GET /api/admin/audit) but no screen. Add "Activity log" under the Admin menu for the OWNER only (role admin, also in a support session; managers, cashiers, waiters and kitchen get 403): newest first, who, when, what (action, item, details shown readably from the JSON), filter by type, support sessions highlighted; the platform owner gets the same log per restaurant on Platform -> Restaurants. Tests: list, filter, manager and cashier get 403, another restaurant's log is never shown, Playwright: a support login appears in the log: done (UI1b, PR #261)

- RN5 rename "restaurant" to "business" in everything users see (Mahesh, 28 Nov): platform list "Restaurants" -> "Businesses", "Add restaurant" -> "Add business", column "Restaurant" -> "Business", owner screens "Restaurant name" -> "Business name", messages, Telegram texts, emails, bills where it says restaurant; English, Gujarati (બિઝનેસ / વ્યવસાય) and Hindi (व्यवसाय); keep "Outlets"; code, database, API paths and C# names stay as they are (Tenant/Restaurant). Search all i18n files and .jsx/.cs user-facing strings; update tests that expect the old words; update the user guide and docs to say business: done (PR #262)

## Phase 1: rename to AllInOne POS
- RN1 app name in one i18n key (app.name) on all screens, title, PWA manifest, index.html: done (PR #253)
- RN2 printed and sent texts (bill and KOT footer, licence texts, Telegram and email, authenticator issuer): done (PR #255)
- RN3 root README and docs titles: done (PR #256)
- RN4 checklist for Mahesh (repo, folder, Docker images, runner paths): done (PR #257)
- RELEASE: v2.4.0 (phase 1: RN1-RN4, FIX1): done (release.req written 28 Nov 09:42 IST)

## Phase 2: capability registry
- C1 capability list in code (keys, names, depends on): done (C1b, PR #259)
- C2 outlet types (templates) owned by the PLATFORM, not the owner: restaurant, cafe, takeaway counter, cloud kitchen, bakery, dairy; each = capabilities + settings preset: done (C2, PR #263)
- C3 capabilities per restaurant (JSON column) + migration backfilling what each restaurant uses today: done (PR #264)
- C4 plans carry allowed capabilities: done (C4)
- C5 shop PC lease carries capabilities, signed; old leases keep today's behaviour: todo
- C6 RequireCapability endpoint filter (403 with message): todo
- C7a reservations behind the filter: todo
- C7b loyalty: todo
- C7c stock and recipes: todo
- C7d tables, kitchen screen, tokens: todo
- C7e QR ordering and feedback: todo
- C8 GET /api/me/capabilities, frontend context, hide menus and routes: todo
- C9 platform Add restaurant: choose the outlet type BEFORE creating; the feature checklist is pre-filled from the type and each feature can be ticked or unticked (custom); saved with the new restaurant; audit logged: todo
- C9b platform edit restaurant: change outlet type or single features later (only the platform), audit logged; existing restaurants become type "custom" with exactly the features they use today: todo
- C9c owner Settings: remove the quick setup (outlet type) buttons and every switch a capability controls (simple counter, kitchen screen, QR ordering, loyalty, reservations...); show "Outlet type: <type> (set by your provider)"; the server refuses (403) owner changes to platform-controlled settings; the owner keeps operational settings (time zone, tokens, receipt, GST): todo
- C10 Playwright smoke: restaurant flow unchanged: todo
- C11 sample (demo) data per outlet type: the platform Demo data button fills data that fits the restaurant's outlet type - restaurant (menu, tables, orders), cafe, takeaway counter, cloud kitchen, bakery (cakes, breads, sweets, simple counter bills), dairy (milk, curd, paneer and chaas sold by kg/litre); only for empty restaurants; tests per type and a negative test (refused when the restaurant already has data): todo
- RELEASE: v2.5.0 (phase 2)

## Phase 3: modules and schemas
- M1 IModule interface, Program.cs loops modules, endpoint list snapshot test: todo
- M2a-h move each module's database model setup into the module (Billing, Inventory, Loyalty, Menu, Orders, Platform, Reservations, Staff), model snapshot unchanged: todo
- STOP: M3 schema plan (which table goes to which schema) - write docs/schema-plan.md, then wait for Mahesh's review
- STOP: M4a-e move tables into schemas (risky, shop PCs) - wait for Mahesh's go
- M5 frontend module registry, lazy loading: todo
- RELEASE: v2.6.0 (phase 3)

## Phase 4 onwards
- STOP: phase 4 (generic core, high risk) - Claude breaks it into tasks first, Mahesh says go
- Later, phase 6 (grocery and supermarket): a BIG starter catalog of the products most kirana and DMart-style stores sell (target several thousand items across staples, packaged food, snacks, beverages, personal care, home care, baby care, stationery): barcode, name (English and Gujarati), brand, pack size, unit, category, HSN, GST and last known MRP; built from Open Food Facts India plus a curated list; a new grocery or supermarket store imports it by category in one click, and the Demo data for these types uses it with sample prices: todo (not before phase 6)
