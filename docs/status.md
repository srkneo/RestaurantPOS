# Roadmap status (the hourly check works through this list in order)

Status: todo / queued / done / parked / STOP (wait for Mahesh's "go" in chat before starting) / RELEASE (automatic: when every task above it is done, write E:\Projects\tasks\queue\release.req containing the version; the PC releases only if the E2E tests on GitHub passed for the current master and outside rush hours; then mark it done and message Mahesh the version and what is in it).
Details of each task: docs/build-plan.md in the repo. Task files are named v2.4-<ID>-<short-name>.txt (alphabetical order = run order).
Rules: one new task per hourly run, only when tasks/queue has no task files and nothing failed. Mark a task done when its PR merged (runner-summary.txt shows "merged").

## In progress
- SUP1 support login (cloud): queued (SUP1f)
- SUP2 shop PC password of the day: todo (after SUP1 is done; changes the signed licence lease: write tests for signature, old leases still valid, code valid only today for that device)

## Release
- RELEASE: v2.3.3 (SUP1, SUP2)

## Phase 1: rename to AllInOne POS
- RN1 app name in one i18n key (app.name) on all screens, title, PWA manifest, index.html: todo
- RN2 printed and sent texts (bill and KOT footer, licence texts, Telegram and email, authenticator issuer): todo
- RN3 root README and docs titles: todo
- RN4 checklist for Mahesh (repo, folder, Docker images, runner paths): todo (write docs/rename-checklist.md only)
- RELEASE: v2.4.0 (phase 1)

## Phase 2: capability registry
- C1 capability list in code (keys, names, depends on): todo
- C2 outlet templates in code (restaurant, cafe, takeaway counter, bakery, dairy) = capabilities + settings presets: todo
- C3 capabilities per restaurant (JSON column) + migration backfilling what each restaurant uses today: todo
- C4 plans carry allowed capabilities: todo
- C5 shop PC lease carries capabilities, signed; old leases keep today's behaviour: todo
- C6 RequireCapability endpoint filter (403 with message): todo
- C7a reservations behind the filter: todo
- C7b loyalty: todo
- C7c stock and recipes: todo
- C7d tables, kitchen screen, tokens: todo
- C7e QR ordering and feedback: todo
- C8 GET /api/me/capabilities, frontend context, hide menus and routes: todo
- C9 platform screen: template and capabilities per restaurant, audit logged: todo
- C10 Playwright smoke: restaurant flow unchanged: todo
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
