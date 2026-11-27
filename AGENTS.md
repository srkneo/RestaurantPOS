# AGENTS.md: start here (for any AI tool or new developer)

AllInOne POS (repo srkneo/RestaurantPOS, being renamed from Restaurant POS): one point-of-sale system for restaurants, bakeries, dairies, grocery (kirana), supermarkets (DMart style), pharmacies and more. Owner: Mahesh Yadav.

**The `docs/` folder is the single source of truth.** Notion is no longer used. Read these first:

1. [docs/status.md](docs/status.md): what is done, queued and next (copied from the live task list after every change).
2. [docs/architecture.md](docs/architecture.md): design (modular monolith, capability plugins, outlet templates), catalog and pricing decisions, phases 0 to 10.
3. [docs/build-plan.md](docs/build-plan.md): how each phase is built, task by task.
4. [docs/supermarket-dmart-style.md](docs/supermarket-dmart-style.md): supermarket features by phase.
5. [docs/runner-lessons.md](docs/runner-lessons.md): known failure patterns and test requirements.
6. [docs/user-guide/README.md](docs/user-guide/README.md): how every screen works, for users.
7. [deploy/RUNBOOK.md](deploy/RUNBOOK.md): server operations and emergencies.

## Stack

ASP.NET Core 10 minimal APIs, EF Core, PostgreSQL, React (Vite), SignalR, NLog, xUnit, Vitest, Playwright, GitHub Actions, Docker Compose on AWS Lightsail with Caddy. Monitoring: Sentry (errors) and Grafana Cloud (metrics, logs via Alloy, alerts to Telegram). Shop PC edition runs offline on a mini PC with a signed licence lease.

## Repo layout

- `Modules/<Area>/`: Billing, Inventory, Loyalty, Menu, Orders, Platform, Reservations, Staff. `Shared/`: auth, AppDbContext partial files, security, telemetry.
- `ClientApp/ClientApp/src/`: React app (`admin/`, `components/`, `ui/`, `i18n/` with en, gu, hi that must have identical keys).
- `RestaurantPOS.Tests/`: xUnit. `e2e/`: Playwright (runs on GitHub on every push). `Migrations/`: EF Core.
- `CONVENTIONS.md`: coding style every change follows.

## Commands

- Server tests: `dotnet test`
- Screen tests: `cd ClientApp/ClientApp && npm test`
- E2E: `cd e2e && npx playwright test`
- Release: a git tag vX.Y.Z; GitHub builds images and deploys.

## How work is done

- Tasks are text files in `E:\Projects\tasks\queue` on Mahesh's PC. A runner script applies them on a feature branch, lets Aider (local LLM) write the code, runs build and all tests, and merges the PR when they pass. Format: see `docs/runner-lessons.md` and `E:\Projects\tasks\templates`.
- Claude writes the tests (unit with negative cases, plus Playwright for user-facing features) and short specs; Aider writes the code.
- An hourly scheduled check fixes failed tasks, takes the next roadmap task, and releases automatically when a group is done and the E2E tests passed.

## Rules for every change

- **Docs change in the same task as the code**: user guide for anything a user sees, architecture or build plan when the design changes, and `docs/status.md`. A change without its docs update is not finished.
- Tests: happy path and negative cases; never weaken or delete a test to make a change pass.
- Never put secrets in the repo. Never edit production data by hand.
- Releases go out outside restaurant rush hours (11:30-15:00 and 19:00-23:00 IST).
