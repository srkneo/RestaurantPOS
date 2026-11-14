# Production deploy

Caddy (HTTPS, automatic certificate) in front of the screens (`web`) and the API (`api`), with PostgreSQL 17.
Address: the server's static IP with dashes plus `.sslip.io`, e.g. `https://13-233-10-20.sslip.io`. No domain needed.

## Releasing

1. Merge to master with all checks green.
2. `git tag v1.5.0` then `git push origin v1.5.0`.
3. The **Release** workflow builds both images, publishes them to GitHub Packages, copies these files to `~/pos` on the server, backs up the database and starts the new version.

To put an older version back: Actions → Release → Run workflow → enter the version (e.g. `1.4.0`).
Database changes are not undone automatically; the backup made before each deploy is in `~/pos/backups`.

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
