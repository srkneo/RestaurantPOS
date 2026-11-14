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
echo "Version $VERSION started."
