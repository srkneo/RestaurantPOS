#!/usr/bin/env bash
# Sets up the POS on a shop PC (a mini PC with Ubuntu or Debian inside the restaurant).
# The POS, kitchen screen and database then run on this PC, so billing works without internet.
# Tills and tablets on the same network open it in a browser.
#
#   curl -fsSLO https://raw.githubusercontent.com/srkneo/RestaurantPOS/master/deploy/shop-install.sh
#   sudo bash shop-install.sh            (newest version)
#   sudo bash shop-install.sh 1.7.0      (a chosen version)
#
# Run it again later with a newer version to update: the database is backed up first and all settings are kept.
# Without questions (for tests): POS_OWNER_EMAIL, POS_OWNER_PASSWORD, POS_VERSION, POS_FILES_DIR (use local files),
# GHCR_TOKEN and GHCR_USER (only while the images are private).
set -euo pipefail

REPO="srkneo/RestaurantPOS"
FILES="docker-compose.prod.yml Caddyfile deploy.sh backup.sh restore-test.sh bill-check.sh"

if [ "$(id -u)" -ne 0 ]; then echo "Run it with sudo:  sudo bash shop-install.sh"; exit 1; fi
export HOME=/root
DIR="$HOME/pos"

say() { printf '\n== %s\n' "$1"; }
json_text() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }
random_hex() { head -c "$1" /dev/urandom | od -An -tx1 | tr -d ' \n'; }

ask() { # ask VARIABLE "Question: " [hidden]
  local name="$1" value=""
  if [ -n "${!name:-}" ]; then return; fi
  if [ ! -r /dev/tty ]; then echo "Set $name before running this script."; exit 1; fi
  if [ -n "${3:-}" ]; then read -r -s -p "$2" value < /dev/tty; echo > /dev/tty; else read -r -p "$2" value < /dev/tty; fi
  printf -v "$name" '%s' "$value"
}

good_password() { # the POS wants 8+ characters with a capital letter, a small letter, a digit and a symbol
  local p="$1"
  local quote="'"
  [ "${#p}" -ge 8 ] || return 1
  case "$p" in *[A-Z]*) ;; *) return 1 ;; esac
  case "$p" in *[a-z]*) ;; *) return 1 ;; esac
  case "$p" in *[0-9]*) ;; *) return 1 ;; esac
  case "$p" in *[!A-Za-z0-9]*) ;; *) return 1 ;; esac
  case "$p" in *"$quote"*) return 1 ;; esac
  return 0
}

VERSION="${POS_VERSION:-${1:-}}"
VERSION="${VERSION#v}"
if [ -z "$VERSION" ]; then
  say "Looking up the newest version"
  VERSION="$(curl -fsSL "https://api.github.com/repos/$REPO/tags?per_page=100" \
    | grep -o '"name": *"v[0-9]*\.[0-9]*\.[0-9]*"' | grep -o '[0-9]*\.[0-9]*\.[0-9]*' | sort -V | tail -n 1 || true)"
  if [ -z "$VERSION" ]; then echo "Could not find the newest version. Give it yourself:  sudo bash shop-install.sh 1.7.0"; exit 1; fi
fi
echo "Version: $VERSION"

if ! command -v docker > /dev/null 2>&1; then
  say "Installing Docker (needs internet, takes a few minutes)"
  for try in 1 2 3; do
    if curl -fsSL https://get.docker.com | sh; then break; fi
    if [ "$try" -eq 3 ]; then echo "Docker could not be installed. Check the internet connection and run this script again."; exit 1; fi
    sleep 10
  done
fi

say "Getting the POS files"
mkdir -p "$DIR/backups"
cd "$DIR"
for f in $FILES; do
  if [ -n "${POS_FILES_DIR:-}" ]; then cp "$POS_FILES_DIR/$f" "$f"
  else curl -fsSL "https://raw.githubusercontent.com/$REPO/v$VERSION/deploy/$f" -o "$f"; fi
done

FIRST_TIME=0
if [ ! -f .env ]; then
  FIRST_TIME=1
  say "First setup: the owner's sign-in"
  ask POS_OWNER_EMAIL "Owner's email (used to sign in): "
  until good_password "${POS_OWNER_PASSWORD:-}"; do
    if [ -n "${POS_OWNER_PASSWORD:-}" ]; then echo "Use at least 8 characters with a capital letter, a small letter, a digit and a symbol (no single quote)."; fi
    POS_OWNER_PASSWORD=""
    ask POS_OWNER_PASSWORD "Owner's password: " hidden
  done
  umask 077
  cat > .env <<ENV
# Settings of this shop PC. Keep this file private.
SHOP_PC=1
SITE_ADDRESS=:80
POSTGRES_PASSWORD=$(random_hex 24)
# Support account that exists only on this PC (random password, not known to anyone).
PLATFORM_EMAIL=support@shop.local
PLATFORM_PASSWORD='Sp#$(random_hex 16)'
ADMIN_EMAIL=$POS_OWNER_EMAIL
ADMIN_PASSWORD='$POS_OWNER_PASSWORD'
ENV
fi
chmod 600 .env

if [ -n "${GHCR_TOKEN:-}" ]; then
  printf '%s' "$GHCR_TOKEN" | docker login ghcr.io -u "${GHCR_USER:-srkneo}" --password-stdin > /dev/null
fi

say "Starting version $VERSION (the first time this downloads about 500 MB)"
bash deploy.sh "$VERSION"

say "Waiting for the POS to answer"
ok=0
for i in $(seq 1 90); do
  if curl -fsS -o /dev/null http://localhost/health; then ok=1; break; fi
  sleep 2
done
if [ "$ok" -ne 1 ]; then
  echo "The POS did not start. See what went wrong with:"
  echo "  cd $DIR && docker compose --env-file .env --env-file version.env -f docker-compose.prod.yml logs --tail 50 api"
  exit 1
fi

if [ "$FIRST_TIME" -eq 1 ]; then
  # Check the owner's sign-in really works, then take the password out of the settings file.
  body="{\"email\":\"$(json_text "$POS_OWNER_EMAIL")\",\"password\":\"$(json_text "$POS_OWNER_PASSWORD")\"}"
  code=000
  for i in $(seq 1 15); do
    code="$(curl -s -o /dev/null -w '%{http_code}' -X POST http://localhost/api/auth/login -H 'Content-Type: application/json' -d "$body" || true)"
    if [ "$code" = "200" ]; then break; fi
    sleep 2
  done
  if [ "$code" != "200" ]; then
    echo "The POS is running, but the owner's sign-in did not work (answer $code)."
    echo "See the reason with:  cd $DIR && docker compose --env-file .env --env-file version.env -f docker-compose.prod.yml logs --tail 50 api"
    exit 1
  fi
  sed -i "s/^ADMIN_PASSWORD=.*/ADMIN_PASSWORD=/" .env
fi

ip="$(hostname -I 2> /dev/null | awk '{print $1}' || true)"
say "The POS is running (version $VERSION)"
echo "On this PC:             http://localhost"
if [ -z "$ip" ]; then ip="<address of this PC>"; fi
echo "On tills and tablets:   http://$ip   (same network or Wi-Fi as this PC)"
if [ "$FIRST_TIME" -eq 1 ]; then echo "Sign in as the owner:   $POS_OWNER_EMAIL"; fi
echo
echo "Tips: give this PC a fixed address in the router, so the link above never changes."
echo "      A backup is made every night at 03:00 in $DIR/backups. Copy that folder to a USB disk regularly."
echo "      To update later:  sudo bash shop-install.sh <new version>"
