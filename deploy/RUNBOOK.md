# Runbook (what to do when something goes wrong)

Server: `ssh` to the Lightsail server, then `cd ~/pos`. Shop PC: open its terminal (WSL) and `cd ~/pos`.
Most problems are found by the request ID: every API answer has an `X-Request-ID` header and error answers carry `requestId`.
Search it in Grafana (Loki) or with `grep <id> logs` inside the API container (`docker exec pos-api-1 grep <id> /app/logs/log.json`).

Never release during restaurant peak hours (lunch 12-15, dinner 19-23). Releases go out early morning or mid-afternoon.

## Site is down

1. `docker ps` - are `pos-caddy-1`, `pos-web-1`, `pos-api-1` and `pos-db-1` all "Up"?
2. A container keeps restarting: `docker logs --tail 100 pos-api-1` (or the one that restarts).
3. Disk full? `df -h` - if `/` is over 90%: `docker system prune -f` (removes old images only; volumes stay).
4. Database not ready: `docker logs --tail 50 pos-db-1`. Never delete the `pgdata` volume.
5. Still down after a release: put the previous version back with `./deploy.sh <previous version>` (the version before is in the release list on GitHub). deploy.sh backs up the database first.
6. Server itself not answering: restart it from the Lightsail console. Everything starts by itself (`restart: unless-stopped`).

## Many errors (5xx alert, Sentry issues, Telegram error lines)

1. Open the Sentry issue: it shows the code line and how many restaurants are hit.
2. Find the request in the log by its request ID and look at the lines around it.
3. Started right after a release: roll back (`./deploy.sh <previous version>`), then fix in a new task.
4. One restaurant only: check its settings and data first; use support access ("view as this restaurant") only with the owner's permission.

## Site is slow (p95 alert)

1. Grafana → Explore → Tempo/traces for `restaurantpos-api`: which endpoint and which database call is slow.
2. `docker logs pos-db-1 2>&1 | grep duration | tail -20` - slow queries over 500 ms.
3. `docker stats --no-stream` - CPU or memory at the limit? A bigger Lightsail plan is the quick fix.
4. Lunch-rush traffic: check the rate limit (429 answers) before raising anything.

## Backup failed (Telegram)

1. `ls -lh backups/` (or the folder in backup.sh) - is last night's file there and not tiny?
2. Run it by hand: `./backup.sh` and read the error.
3. Disk full: see "Site is down" step 3. Never delete the newest backup.
4. A restore test runs weekly (restore-test.sh); restoring for real is in README.md ("Restoring for real").

## Bill check found a mismatch (Telegram)

1. Stop and look before anything else: wrong bill totals are the worst kind of bug.
2. Note the restaurant, bill numbers and the request IDs; compare the bill lines with the order lines.
3. Do not edit bills in the database. Fix the code, then issue credit notes where a customer was charged wrongly.

## Shop PC problems

- No internet: the shop keeps billing; orders sync later. Nothing to do.
- Licence warning: renew from /platform → Shop licences; the shop gets it at the next connection.
- Update failed: it rolls back by itself; the status is on the About page. Pause updates for that shop from /platform if needed.
- Backup copy warning (USB/NAS): plug the disk back in; the next copy runs that night.

## Platform sign-in code does not arrive

1. Normal case: the authenticator app is on for the platform account (/account/security), so sign-in asks for the app code, not Telegram.
2. Phone lost: type one of the printed backup codes instead of the app code (each works once). Then turn the app off and on again to get new codes.
3. No app and no backup codes: on the server, put a `#` in front of `TELEGRAM_TOKEN` in `~/pos/.env` and run `./deploy.sh <current version>`.
   Sign-in then needs only the password. Sign in, turn on the app, remove the `#` and run deploy.sh again.

## Support contacts and hours

Shown on the About page and set from /platform. Support hours: 8-10 AM and 7-10 PM.
