#!/usr/bin/env bash
# Shop PC only: keeps the POS on the newest release by itself. Cron runs it every night at 03:30, and every
# 2 minutes to see whether the owner pressed "Update now" in the app. It uses shop-install.sh of the new version,
# so the database is backed up first and a version that does not start is rolled back (deploy.sh).
#   ./auto-update.sh nightly      the nightly check
#   ./auto-update.sh requested    only when the app asked for it (the file update/request exists)
# Support can pause updates or keep a shop on one version; the app writes that into update/policy.
# The result is written to update/status.json, which the app shows on the Licence page.
# For tests: POS_UPDATE_DRY_RUN=1 (decide and write the status, but do not update), GITHUB_TOKEN (signed-in lookups).
set -uo pipefail
# Cron starts jobs with a short PATH that misses /usr/sbin (blkid, mount). Without this the USB disk is never found.
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:${PATH:-}"

# Everything is inside main, so bash has read the whole script before an update replaces this file.
main() {
  cd "$(dirname "$0")" || exit 1
  local repo="srkneo/RestaurantPOS" upd="update" trigger="${1:-nightly}"
  mkdir -p "$upd"
  chmod 777 "$upd"

  if [ "$trigger" = "requested" ]; then
    [ -f "$upd/request" ] || exit 0
    rm -f "$upd/request"
  fi

  # One run at a time.
  exec 9> "$upd/.lock"
  flock -n 9 || exit 0

  current="$(sed -n 's/^POS_VERSION=//p' version.env 2> /dev/null | head -n 1)"
  local mode pinned target="" tags v before log script waiting=""
  mode="$(sed -n 's/^mode=//p' "$upd/policy" 2> /dev/null | head -n 1)"
  pinned="$(sed -n 's/^version=//p' "$upd/policy" 2> /dev/null | head -n 1)"
  mode="${mode:-auto}"
  UPD="$upd"
  TRIGGER="$trigger"

  if [ "$mode" = "paused" ]; then
    status paused "Updates are paused by support."
    exit 0
  fi

  if [ "$mode" = "pinned" ] && [ -n "$pinned" ]; then
    target="$pinned"
  else
    local auth=()
    if [ -n "${GITHUB_TOKEN:-}" ]; then auth=(-H "Authorization: Bearer $GITHUB_TOKEN"); fi
    if ! tags="$(curl -fsSL --connect-timeout 20 --max-time 60 "${auth[@]}" "https://api.github.com/repos/$repo/tags?per_page=100" 2> /dev/null)"; then
      status offline "Could not reach the internet to look for updates. Billing is not affected."
      exit 0
    fi
    # The newest release whose images are published (right after a release they may still be building).
    # Staged rollout: the nightly update takes a release only after it has been out for 2 days (on the cloud and
    # on early shops), so a bad release reaches few shops. Early shops and "Update now" take the newest at once.
    local soak=$((2 * 86400))
    if [ "$mode" = "early" ] || [ "$trigger" = "requested" ]; then soak=0; fi
    if [ -n "${POS_UPDATE_SOAK_SECONDS:-}" ]; then soak="$POS_UPDATE_SOAK_SECONDS"; fi
    for v in $(printf '%s' "$tags" | grep -o '"name": *"v[0-9]*\.[0-9]*\.[0-9]*"' | grep -o '[0-9]*\.[0-9]*\.[0-9]*' | sort -rV); do
      if [ "$soak" -gt 0 ] && ! old_enough "$v" "$soak" "$tags"; then waiting="${waiting:-$v}"; continue; fi
      if has_images "$v"; then target="$v"; break; fi
    done
    if [ -z "$target" ] && [ -n "$waiting" ]; then
      status up-to-date "Version $waiting is new; this PC takes it after it has been out for 2 days." "$waiting"
      exit 0
    fi
    if [ -z "$target" ]; then
      status offline "No finished release could be found right now. It is tried again later."
      exit 0
    fi
  fi

  # Never goes back to an older version by itself (database changes are not undone).
  if ! newer "$target" "$current"; then
    if [ "$mode" = "pinned" ]; then status up-to-date "Support keeps this PC on version $current." "$target"
    elif [ -n "$waiting" ]; then status up-to-date "Version $current is in use; $waiting comes after it has been out for 2 days." "$target"
    else status up-to-date "Version $current is the newest." "$target"; fi
    exit 0
  fi
  if [ -n "${POS_UPDATE_DRY_RUN:-}" ]; then
    status would-update "Would update from $current to $target (test run)." "$target"
    exit 0
  fi

  status updating "Updating from $current to $target. The POS restarts for about a minute." "$target"
  log="$upd/last-update.log"
  script="$(mktemp)"
  if ! curl -fsSL --connect-timeout 20 --max-time 120 "https://raw.githubusercontent.com/$repo/v$target/deploy/shop-install.sh" -o "$script"; then
    rm -f "$script"
    status failed "Could not download the update. Billing is not affected; it is tried again next night." "$target"
    exit 1
  fi
  before="$current"
  if POS_VERSION="$target" bash "$script" > "$log" 2>&1; then
    current="$target"
    status updated "Updated from $before to $target." "$target"
  elif grep -q "is running again" "$log"; then
    status rolled-back "Version $target did not start, so $before was put back. Nothing was lost." "$target"
  else
    current="$(sed -n 's/^POS_VERSION=//p' version.env 2> /dev/null | head -n 1)"
    status failed "The update to $target did not finish. Details: $PWD/$log" "$target"
  fi
  rm -f "$script"
}

json_text() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr -d '\r\n'; }

# status <result> <message> [target]: written whole, then moved into place, so the app never reads half a file.
status() {
  local tmp="$UPD/status.json.tmp"
  printf '{"checkedAt":"%s","trigger":"%s","current":"%s","target":"%s","result":"%s","message":"%s"}\n' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$TRIGGER" "$(json_text "$current")" "$(json_text "${3:-}")" "$1" "$(json_text "$2")" > "$tmp"
  chmod 644 "$tmp"
  mv "$tmp" "$UPD/status.json"
  echo "$(date -u +%FT%TZ) $1: $2"
}

# old_enough V SECONDS TAGS_JSON: release V has been out for at least SECONDS (date of its tag's commit).
# When the date cannot be found it counts as old enough, so a lookup problem never blocks updates for good.
old_enough() {
  local sha when epoch auth=()
  sha="$(printf '%s' "$3" | tr -d ' \n\r' | grep -o "\"name\":\"v$1\",[^}]*\"sha\":\"[0-9a-f]*\"" | grep -o '[0-9a-f]\{40\}' | head -n 1)"
  [ -n "$sha" ] || return 0
  if [ -n "${GITHUB_TOKEN:-}" ]; then auth=(-H "Authorization: Bearer $GITHUB_TOKEN"); fi
  when="$(curl -fsSL --connect-timeout 20 --max-time 60 "${auth[@]}" "https://api.github.com/repos/srkneo/RestaurantPOS/commits/$sha" 2> /dev/null \
    | grep -o '"date": *"[^"]*"' | tail -n 1 | grep -o '[0-9][0-9T:.-]*Z')" || return 0
  [ -n "$when" ] || return 0
  epoch="$(date -d "$when" +%s 2> /dev/null)" || return 0
  [ $(( $(date +%s) - epoch )) -ge "$2" ]
}

# newer A B: A is a higher version than B.
newer() { [ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | tail -n 1)" = "$1" ]; }

has_images() {
  docker manifest inspect "ghcr.io/srkneo/restaurantpos-api:$1" > /dev/null 2>&1 \
    && docker manifest inspect "ghcr.io/srkneo/restaurantpos-web:$1" > /dev/null 2>&1
}

current=""
UPD="update"
TRIGGER=""
main "$@"
exit $?
