#!/usr/bin/env bash
# Weekly proof that backups can be restored: loads the newest nightly backup (or the file given)
# into a throwaway database, checks it has restaurants and users, then removes it.
# Sends the result to Telegram, so a weekly ✅ also shows that backups are running.
set -euo pipefail
cd "$(dirname "$0")"

env_value() { if [ -f .env ]; then grep -E "^$1=" .env | tail -n 1 | cut -d= -f2- || true; fi; }

notify() {
  local token chat
  token="$(env_value TELEGRAM_TOKEN)"; chat="$(env_value TELEGRAM_CHAT_ID)"
  if [ -n "$token" ] && [ -n "$chat" ]; then
    curl -sS --max-time 10 -X POST "https://api.telegram.org/bot${token}/sendMessage" \
      -d chat_id="$chat" --data-urlencode text="$1" > /dev/null || true
  fi
}
trap 'notify "❌ RestaurantPOS restore test FAILED on $(hostname). Check ~/pos/backups/restore-test.log"' ERR

file="${1:-$(ls -1t backups/nightly/pos-*.dump 2>/dev/null | head -n 1 || true)}"
if [ -z "$file" ] || [ ! -s "$file" ]; then echo "No backup file found"; false; fi

age_hours=$(( ( $(date +%s) - $(stat -c %Y "$file") ) / 3600 ))
container="pos-restore-test-$$"
cleanup() { docker rm -f "$container" > /dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --name "$container" -e POSTGRES_PASSWORD=restore-test -e POSTGRES_DB=restore postgres:17 > /dev/null
# Wait for the real server (the first-start setup server does not listen on TCP).
for i in $(seq 1 60); do
  if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres -d restore > /dev/null 2>&1; then break; fi
  if [ "$i" -eq 60 ]; then echo "Test database did not start"; false; fi
  sleep 2
done

docker exec -i "$container" pg_restore -U postgres -d restore --no-owner --no-privileges --exit-on-error < "$file"

query() { docker exec "$container" psql -U postgres -d restore -tAc "$1"; }
tenants="$(query 'SELECT count(*) FROM "Tenants"')"
users="$(query 'SELECT count(*) FROM "AspNetUsers"')"
migration="$(query 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1')"

if [ "$tenants" -lt 1 ] || [ "$users" -lt 1 ]; then echo "Restored database has no restaurants or users"; false; fi

message="✅ RestaurantPOS restore test passed: $(basename "$file") (${age_hours}h old), $tenants restaurants, $users users, schema $migration"
echo "$(date -u +%FT%TZ) $message"
notify "$message"
if [ "$age_hours" -gt 30 ]; then
  notify "⚠️ RestaurantPOS: the newest backup is ${age_hours} hours old. Is the nightly backup running?"
fi
