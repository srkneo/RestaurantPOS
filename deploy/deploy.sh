#!/usr/bin/env bash
# Runs on the server: ./deploy.sh 1.5.0
# Backs up the database, then pulls and starts that version. The app applies database changes when it starts.
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:?Usage: ./deploy.sh <version, e.g. 1.5.0>}"
COMPOSE=(docker compose --env-file .env --env-file version.env -f docker-compose.prod.yml)

if [ ! -f .env ]; then
  echo ".env is missing. Copy .env.example to .env and fill it in."
  exit 1
fi

mkdir -p backups
if [ -n "$(docker ps -q -f name=^pos-db-1$)" ]; then
  file="backups/before-$VERSION-$(date +%Y%m%d-%H%M%S).sql.gz"
  docker exec pos-db-1 pg_dump -U pos -d restaurant_pos | gzip > "$file"
  echo "Database backed up to $file"
  # Keep the 10 newest backups made before deploys.
  ls -1t backups/before-*.sql.gz | tail -n +11 | xargs -r rm --
fi

echo "POS_VERSION=$VERSION" > version.env
"${COMPOSE[@]}" pull
"${COMPOSE[@]}" up -d --remove-orphans
docker image prune -f > /dev/null

# Nightly backup at 03:00 India time (21:30 UTC); restore test on Sunday 03:30 India time.
chmod +x backup.sh restore-test.sh
{
  crontab -l 2>/dev/null | grep -v -e 'pos/backup.sh' -e 'pos/restore-test.sh' || true
  echo "30 21 * * * $HOME/pos/backup.sh >> $HOME/pos/backups/backup.log 2>&1"
  echo "0 22 * * 6 $HOME/pos/restore-test.sh >> $HOME/pos/backups/restore-test.log 2>&1"
} | crontab -
echo "Version $VERSION started."
