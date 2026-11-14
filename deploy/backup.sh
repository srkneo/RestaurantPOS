#!/usr/bin/env bash
# Nightly database backup (cron, 03:00 India time). Keeps 7 days on the server and copies each one to S3.
# S3 is used when BACKUP_S3_BUCKET and the AWS_* keys are set in .env. A failure sends a Telegram message.
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
trap 'notify "❌ RestaurantPOS nightly backup FAILED on $(hostname). Check ~/pos/backups/backup.log"' ERR

DB_CONTAINER="${DB_CONTAINER:-pos-db-1}"
BUCKET="$(env_value BACKUP_S3_BUCKET)"

mkdir -p backups/nightly
name="pos-$(date -u +%Y%m%d-%H%M%S).dump"
file="backups/nightly/$name"

# Custom format: compressed, and pg_restore can restore all or part of it.
docker exec "$DB_CONTAINER" pg_dump -U pos -d restaurant_pos -Fc > "$file.part"
mv "$file.part" "$file"
if [ ! -s "$file" ]; then echo "Backup file is empty"; false; fi
echo "$(date -u +%FT%TZ) saved $file ($(du -h "$file" | cut -f1))"

if [ -n "$BUCKET" ]; then
  docker run --rm -v "$PWD/backups/nightly:/backups:ro" \
    -e AWS_ACCESS_KEY_ID="$(env_value AWS_ACCESS_KEY_ID)" \
    -e AWS_SECRET_ACCESS_KEY="$(env_value AWS_SECRET_ACCESS_KEY)" \
    -e AWS_DEFAULT_REGION="$(env_value AWS_DEFAULT_REGION)" \
    amazon/aws-cli s3 cp "/backups/$name" "s3://$BUCKET/nightly/$name" --only-show-errors
  echo "$(date -u +%FT%TZ) copied to s3://$BUCKET/nightly/$name"
else
  echo "BACKUP_S3_BUCKET is not set: backup kept on this server only."
fi

find backups/nightly -name 'pos-*.dump' -mtime +7 -delete
