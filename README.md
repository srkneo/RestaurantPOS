# AllInOne POS

One point-of-sale system for restaurants, cafes, bakeries, dairies, grocery (kirana) shops, supermarkets and more: billing counter, kitchen screen, token screen, menu, tables, stock, reservations, loyalty, staff, GST bills, day close and reports. It runs in any browser (tablet, phone, laptop or PC), in the cloud or offline on a shop PC.

The GitHub repo is still called `srkneo/RestaurantPOS`; the repo, folder and Docker image names change later (see the rename checklist task RN4 in [docs/status.md](docs/status.md)).

## Start here

- [AGENTS.md](AGENTS.md): short guide for any AI tool or new developer (stack, layout, commands, rules)
- [docs/README.md](docs/README.md): all documentation; the `docs/` folder is the single source of truth
- [docs/status.md](docs/status.md): what is done, queued and next
- [docs/user-guide/README.md](docs/user-guide/README.md): how every screen works, for users
- [deploy/RUNBOOK.md](deploy/RUNBOOK.md): server operations and emergencies

## Stack

ASP.NET Core 10 minimal APIs, EF Core, PostgreSQL, React (Vite), SignalR, NLog, xUnit, Vitest, Playwright, GitHub Actions, Docker Compose on AWS Lightsail with Caddy. Monitoring: Sentry and Grafana Cloud.

## Commands

- Server tests: `dotnet test`
- Screen tests: `cd ClientApp/ClientApp && npm test`
- E2E: `cd e2e && npx playwright test`
- Run locally: see [docs/daily-startup-checklist.md](docs/daily-startup-checklist.md)
- Release: a git tag vX.Y.Z; GitHub builds the images and deploys.
