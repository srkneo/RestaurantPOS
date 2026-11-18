#!/usr/bin/env bash
# Runs on the server or shop PC: ./deploy.sh 1.7.2
# Backs up the database, then pulls and starts that version. The app applies database changes when it starts.
# If the new version is not ready within 3 minutes, the previous version and its database are put back by themselves.
# For tests only: POS_SKIP_PULL=1 (use images already on this machine), POS_READY_WAIT=<seconds> (default 180).
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:?Usage: ./deploy.sh <version, e.g. 1.7.2>}"
COMPOSE=(docker compose --env-file .env --env-file version.env -f docker-compose.prod.yml)
DB=pos-db-1
READY_WAIT="${POS_READY_WAIT:-180}"

if [ ! -f .env ]; then
  echo ".env is missing. Copy .env.example to .env and fill it in."
  exit 1
fi

env_value() { grep -E "^$1=" .env | tail -n 1 | cut -d= -f2- || true; }
notify() {
  local token chat
  token="$(env_value TELEGRAM_TOKEN)"; chat="$(env_value TELEGRAM_CHAT_ID)"
  if [ -n "$token" ] && [ -n "$chat" ]; then
    curl -sS --max-time 10 -X POST "https://api.telegram.org/bot${token}/sendMessage" \
      -d chat_id="$chat" --data-urlencode text="$1" > /dev/null || true
  fi
}

# Ready = the API answers and reaches the database, and the screens answer. Checked from inside Caddy,
# so it works the same with HTTPS on the server and plain HTTP on a shop PC.
is_ready() {
  docker exec pos-caddy-1 wget -q -T 5 -O /dev/null http://api:8080/health/ready > /dev/null 2>&1 \
    && docker exec pos-caddy-1 wget -q -T 5 -O /dev/null http://web:80/ > /dev/null 2>&1
}
wait_ready() {
  local waited=0
  while [ "$waited" -lt "$READY_WAIT" ]; do
    if is_ready; then return 0; fi
    sleep 3
    waited=$((waited + 3))
  done
  return 1
}

previous=""
if [ -f version.env ]; then previous="$(sed -n 's/^POS_VERSION=//p' version.env | head -n 1)"; fi
# A first install has no version.env yet, and docker compose needs the file to exist.
if [ ! -f version.env ]; then echo "POS_VERSION=$VERSION" > version.env; fi

mkdir -p backups update
# update/ is shared with the app (the "Update now" request, the update status and support's update policy).
chmod 777 update
backup=""
if [ -n "$(docker ps -q -f name=^${DB}$)" ]; then
  backup="backups/before-$VERSION-$(date +%Y%m%d-%H%M%S).dump"
  docker exec "$DB" pg_dump -U pos -d restaurant_pos -Fc > "$backup.part"
  mv "$backup.part" "$backup"
  echo "Database backed up to $backup"
  # Keep the 10 newest backups made before deploys (older .sql.gz ones from earlier versions too).
  ls -1t backups/before-* 2> /dev/null | tail -n +11 | xargs -r rm --
fi

# Pull first: if the new version cannot be downloaded, nothing changes and the running version stays.
if [ -z "${POS_SKIP_PULL:-}" ]; then
  POS_VERSION="$VERSION" "${COMPOSE[@]}" pull
fi
echo "POS_VERSION=$VERSION" > version.env
"${COMPOSE[@]}" up -d --remove-orphans

if ! wait_ready; then
  echo "Version $VERSION did not become ready within $READY_WAIT seconds. Last lines of its log:"
  "${COMPOSE[@]}" logs --tail 40 api || true
  if [ -n "$previous" ] && [ "$previous" != "$VERSION" ]; then
    echo "Putting version $previous back."
    "${COMPOSE[@]}" stop api web
    if [ -n "$backup" ]; then
      # A fresh database from the backup, so tables the new version added cannot get in the way later.
      docker exec "$DB" psql -U pos -d postgres -q -c 'DROP DATABASE IF EXISTS restaurant_pos WITH (FORCE)'
      docker exec "$DB" psql -U pos -d postgres -q -c 'CREATE DATABASE restaurant_pos'
      # pg_restore can report harmless warnings; whether it worked is shown by the old version becoming ready below.
      docker exec -i "$DB" pg_restore -U pos -d restaurant_pos --no-owner < "$backup" || echo "pg_restore reported problems (see above)."
      echo "Database put back from $backup"
    fi
    echo "POS_VERSION=$previous" > version.env
    "${COMPOSE[@]}" up -d --remove-orphans
    if wait_ready; then
      echo "Version $previous is running again. Nothing was lost; the update can be tried again later."
      notify "⚠️ RestaurantPOS $VERSION did not start on $(hostname). Version $previous was put back and is running."
    else
      echo "Version $previous did not start either. Check the log above, or restore a backup (see README)."
      notify "❌ RestaurantPOS update to $VERSION FAILED on $(hostname), and $previous did not start either. Needs attention now."
    fi
  else
    notify "❌ RestaurantPOS $VERSION did not start on $(hostname)."
  fi
  exit 1
fi
docker image prune -f > /dev/null

# Nightly backup at 03:00 India time; restore test on Sunday 03:30; bill check every night at 04:00.
# Cloud servers run on UTC (03:00 in India is 21:30 UTC). A shop PC (SHOP_PC=1 in .env) keeps its own local time.
backup_at="30 21 * * *"; restore_at="0 22 * * 6"; check_at="30 22 * * *"
if grep -q '^SHOP_PC=1' .env; then backup_at="0 3 * * *"; restore_at="30 3 * * 0"; check_at="0 4 * * *"; fi
chmod +x backup.sh restore-test.sh bill-check.sh
if [ -f auto-update.sh ]; then chmod +x auto-update.sh; fi
if [ -f copy-backup.sh ]; then chmod +x copy-backup.sh; fi
{
  crontab -l 2>/dev/null | grep -v -e 'pos/backup.sh' -e 'pos/restore-test.sh' -e 'pos/bill-check.sh' -e 'pos/auto-update.sh' -e 'pos/copy-backup.sh' || true
  echo "$backup_at $HOME/pos/backup.sh >> $HOME/pos/backups/backup.log 2>&1"
  echo "$restore_at $HOME/pos/restore-test.sh >> $HOME/pos/backups/restore-test.log 2>&1"
  echo "$check_at $HOME/pos/bill-check.sh >> $HOME/pos/backups/bill-check.log 2>&1"
  # Shop PC: update itself every night at 03:30 (after the backup), and check every 2 minutes for "Update now".
  if grep -q '^SHOP_PC=1' .env && [ -f auto-update.sh ]; then
    echo "30 3 * * * $HOME/pos/auto-update.sh nightly >> $HOME/pos/backups/auto-update.log 2>&1"
    echo "*/2 * * * * $HOME/pos/auto-update.sh requested >> $HOME/pos/backups/auto-update.log 2>&1"
  fi
  # Shop PC: "Back up now" from the app (checked every 2 minutes).
  if grep -q '^SHOP_PC=1' .env && [ -f copy-backup.sh ]; then
    echo "*/2 * * * * $HOME/pos/copy-backup.sh requested >> $HOME/pos/backups/backup.log 2>&1"
  fi
} | crontab -
echo "Version $VERSION started."
