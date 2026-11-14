# Production deploy

Caddy (HTTPS, automatic certificate) in front of the screens (`web`) and the API (`api`), with PostgreSQL 17.
Address: the server's static IP with dashes plus `.sslip.io`, e.g. `https://13-233-10-20.sslip.io`. No domain needed.

## Releasing

1. Merge to master with all checks green.
2. `git tag v1.5.0` then `git push origin v1.5.0`.
3. The **Release** workflow builds both images, publishes them to GitHub Packages, copies these files to `~/pos` on the server, backs up the database and starts the new version.

To put an older version back: Actions → Release → Run workflow → enter the version (e.g. `1.4.0`).
Database changes are not undone automatically; use a backup (below) if needed.

## Backups

| What | When | Where | Kept |
|---|---|---|---|
| Nightly backup (`backup.sh`) | 03:00 India time | `~/pos/backups/nightly` and `s3://<bucket>/nightly` | 7 days on the server, 30 days in S3 |
| Before each deploy (`deploy.sh`) | every release | `~/pos/backups/before-*.sql.gz` | newest 10 |
| Restore test (`restore-test.sh`) | Sunday 03:30 India time | throwaway database, removed after | Telegram ✅ or ❌ |

A failed backup or restore test sends a Telegram message. No ✅ on Sunday morning means something is wrong.
Logs: `~/pos/backups/backup.log` and `~/pos/backups/restore-test.log`.

### Restoring for real

Only when the live data is lost or damaged. Everything entered after the backup is lost.

```
cd ~/pos
C="docker compose --env-file .env --env-file version.env -f docker-compose.prod.yml"
./restore-test.sh backups/nightly/pos-YYYYMMDD-HHMMSS.dump      # 1. check the file first
./backup.sh                                                    # 2. keep a copy of the current state
$C stop api                                                    # 3. stop the app
docker exec -i pos-db-1 pg_restore -U pos -d restaurant_pos --clean --if-exists --no-owner \
  < backups/nightly/pos-YYYYMMDD-HHMMSS.dump                   # 4. restore
$C start api                                                   # 5. start the app
```

A backup from S3 (if the server itself is lost), into `~/pos/backups/nightly`:

```
docker run --rm -v ~/pos/backups/nightly:/b --env-file ~/pos/.env amazon/aws-cli \
  s3 cp s3://<bucket>/nightly/pos-YYYYMMDD-HHMMSS.dump /b/
```

## One-time server setup (done in V5)

- Ubuntu with Docker and the compose plugin; the deploy user is in the `docker` group.
- Firewall: ports 22, 80 and 443 only.
- `~/pos/.env` made from `.env.example` (`chmod 600 .env`).
- Repository secrets: `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`, `SITE_URL`.

## Useful commands on the server

```
cd ~/pos
docker compose --env-file .env --env-file version.env -f docker-compose.prod.yml ps
docker compose --env-file .env --env-file version.env -f docker-compose.prod.yml logs --tail 100 api
```
