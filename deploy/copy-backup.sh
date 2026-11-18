#!/usr/bin/env bash
# Shop PC: copies the newest nightly backup off this PC, to a USB disk or a NAS, so a broken PC disk loses nothing.
#   USB disk: format it (exFAT or ext4) with the name POSBACKUP and leave it plugged in. It is mounted only while copying.
#   NAS or another disk: set BACKUP_COPY_DIR=/path in .env (a folder that is already mounted, e.g. through /etc/fstab).
# The result goes to update/backup-status.json; the app warns the owner when the last copy is more than 2 days old.
#   ./copy-backup.sh              copy the newest backup (backup.sh runs this after each nightly backup)
#   ./copy-backup.sh requested    only when the owner pressed "Back up now" (update/backup-request): back up, then copy
set -uo pipefail
# Cron starts jobs with a short PATH that misses /usr/sbin (blkid, mount). Without this the USB disk is never found.
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:${PATH:-}"

main() {
  cd "$(dirname "$0")" || exit 1
  mkdir -p update
  chmod 777 update

  if [ "${1:-}" = "requested" ]; then
    [ -f update/backup-request ] || exit 0
    rm -f update/backup-request
    exec 8> update/.backup-lock
    flock -n 8 || exit 0
    # backup.sh runs this script again (without "requested") to make the copy.
    bash backup.sh
    exit $?
  fi

  local latest target="" where="" mnt="" mounted="" dev dir name
  latest="$(ls -1t backups/nightly/pos-*.dump 2> /dev/null | head -n 1)"
  if [ -z "$latest" ]; then
    status failed "" "" "There is no backup to copy yet."
    exit 1
  fi

  dev="$(blkid -L POSBACKUP 2> /dev/null || true)"
  if [ -n "$dev" ]; then
    where="USB disk"
    mnt="$(findmnt -n -o TARGET --source "$dev" 2> /dev/null | head -n 1)"
    if [ -z "$mnt" ]; then
      mkdir -p /mnt/posbackup
      if mount "$dev" /mnt/posbackup; then mnt=/mnt/posbackup; mounted=1; fi
    fi
    if [ -n "$mnt" ]; then target="$mnt/pos-backups"; fi
  else
    dir="$(grep -E '^BACKUP_COPY_DIR=' .env 2> /dev/null | tail -n 1 | cut -d= -f2-)"
    if [ -n "$dir" ] && [ -d "$dir" ]; then target="$dir"; where="backup folder"; fi
  fi

  if [ -z "$target" ]; then
    status no-disk "$latest" "" "No backup disk found. Plug in the USB disk named POSBACKUP."
    exit 0
  fi

  name="$(basename "$latest")"
  local rc=0
  if mkdir -p "$target" && cp "$latest" "$target/$name.part" && mv -f "$target/$name.part" "$target/$name" && sync \
    && [ "$(stat -c %s "$latest")" = "$(stat -c %s "$target/$name")" ]; then
    # Keep the 14 newest copies on the disk.
    ls -1t "$target"/pos-*.dump 2> /dev/null | tail -n +15 | xargs -r rm -f --
    COPIED=1
    status copied "$latest" "$where" "Copied $name to the $where."
  else
    rm -f "$target/$name.part"
    status failed "$latest" "$where" "Could not copy to the $where. Is it full or write-protected?"
    rc=1
  fi
  if [ -n "$mounted" ]; then umount /mnt/posbackup || true; fi
  exit "$rc"
}

json_text() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr -d '\r\n'; }

# status <result> <backup file> <where> <message>. lastCopiedAt is kept from earlier runs until a new copy succeeds.
status() {
  local now backup_at="" last="" tmp="update/backup-status.json.tmp"
  now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  if [ -n "$2" ] && [ -f "$2" ]; then backup_at="$(date -u -r "$2" +%Y-%m-%dT%H:%M:%SZ)"; fi
  if [ "$COPIED" = "1" ]; then last="$now"
  else last="$(grep -o '"lastCopiedAt":"[^"]*"' update/backup-status.json 2> /dev/null | cut -d'"' -f4)"; fi
  printf '{"checkedAt":"%s","backupAt":"%s","lastCopiedAt":"%s","target":"%s","result":"%s","message":"%s"}\n' \
    "$now" "$backup_at" "$last" "$(json_text "$3")" "$1" "$(json_text "$4")" > "$tmp"
  chmod 644 "$tmp"
  mv "$tmp" update/backup-status.json
  echo "$now backup copy $1: $4"
}

COPIED=0
main "$@"
exit $?
