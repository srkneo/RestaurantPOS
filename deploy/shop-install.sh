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
# Licence: POS_LICENCE_KEY (the key from support), POS_LICENCE_SERVER (another licence server address),
# POS_NO_LICENCE=1 (install without a licence, for tests only).
set -euo pipefail

REPO="srkneo/RestaurantPOS"
# Downloads give up on a dead connection instead of hanging, and try again a few times.
fetch() { curl -fsSL --connect-timeout 30 --max-time 600 --retry 3 --retry-delay 3 "$@"; }
FILES="docker-compose.prod.yml Caddyfile deploy.sh backup.sh restore-test.sh bill-check.sh auto-update.sh copy-backup.sh"
LICENCE_KEY_FILE="licence-public.key"
DEFAULT_LICENCE_URL="https://13-236-102-22.sslip.io"

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
  # The newest release whose images are published (right after a release they may still be building).
  # Before Docker is installed this cannot be checked, so the newest tag is taken.
  VERSION=""
  for v in $(fetch "https://api.github.com/repos/$REPO/tags?per_page=100" \
      | grep -o '"name": *"v[0-9]*\.[0-9]*\.[0-9]*"' | grep -o '[0-9]*\.[0-9]*\.[0-9]*' | sort -rV || true); do
    if command -v docker > /dev/null 2>&1 && ! { docker manifest inspect "ghcr.io/srkneo/restaurantpos-api:$v" > /dev/null 2>&1 \
      && docker manifest inspect "ghcr.io/srkneo/restaurantpos-web:$v" > /dev/null 2>&1; }; then continue; fi
    VERSION="$v"
    break
  done
  if [ -z "$VERSION" ]; then echo "Could not find the newest version. Give it yourself:  sudo bash shop-install.sh 1.7.0"; exit 1; fi
fi
echo "Version: $VERSION"

if ! command -v docker > /dev/null 2>&1; then
  say "Installing Docker (needs internet, takes a few minutes)"
  for try in 1 2 3; do
    if fetch https://get.docker.com | sh; then break; fi
    if [ "$try" -eq 3 ]; then echo "Docker could not be installed. Check the internet connection and run this script again."; exit 1; fi
    sleep 10
  done
fi

say "Getting the POS files"
mkdir -p "$DIR/backups"
cd "$DIR"
for f in $FILES; do
  if [ -n "${POS_FILES_DIR:-}" ]; then cp "$POS_FILES_DIR/$f" "$f"
  else
    fetch "https://raw.githubusercontent.com/$REPO/v$VERSION/deploy/$f" -o "$f" \
      || { echo "Could not download $f. Check the internet connection and run this script again."; exit 1; }
  fi
done

# The public key that checks licences (it is not secret). It is committed in the repo as deploy/licence-public.key.
LICENCE_ON=0
if [ -z "${POS_NO_LICENCE:-}" ]; then
  if [ -n "${POS_FILES_DIR:-}" ]; then
    if [ -f "$POS_FILES_DIR/$LICENCE_KEY_FILE" ]; then cp "$POS_FILES_DIR/$LICENCE_KEY_FILE" "$LICENCE_KEY_FILE"; fi
  else
    fetch "https://raw.githubusercontent.com/$REPO/v$VERSION/deploy/$LICENCE_KEY_FILE" -o "$LICENCE_KEY_FILE" 2> /dev/null || rm -f "$LICENCE_KEY_FILE"
  fi
  if [ ! -s "$LICENCE_KEY_FILE" ]; then
    echo "The licence key file ($LICENCE_KEY_FILE) is missing for version $VERSION, so no licence can be checked."
    echo "Use a newer version, or set POS_NO_LICENCE=1 to install without a licence (tests only)."
    exit 1
  fi
  LICENCE_ON=1
fi

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
  if [ "$LICENCE_ON" -eq 1 ] && [ -z "${POS_LICENCE_KEY:-}" ] && { : < /dev/tty; } 2> /dev/null; then
    read -r -p "Licence key (from support; press Enter to add it later in the app): " POS_LICENCE_KEY < /dev/tty
  fi
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

# Licence settings are refreshed on every run, so a new public key arrives with an update. The server address is kept.
if [ "$LICENCE_ON" -eq 1 ]; then
  current_url="$(sed -n 's/^LICENCE_SERVER_URL=//p' .env | head -n 1)"
  sed -i '/^LICENCE_REQUIRED=/d;/^LICENCE_PUBLIC_KEY=/d;/^LICENCE_SERVER_URL=/d' .env
  {
    echo "LICENCE_REQUIRED=true"
    echo "LICENCE_PUBLIC_KEY=$(tr -d ' \r\n' < "$LICENCE_KEY_FILE")"
    echo "LICENCE_SERVER_URL=${POS_LICENCE_SERVER:-${current_url:-$DEFAULT_LICENCE_URL}}"
  } >> .env
fi

if [ -n "${GHCR_TOKEN:-}" ]; then
  printf '%s' "$GHCR_TOKEN" | docker login ghcr.io -u "${GHCR_USER:-srkneo}" --password-stdin > /dev/null
fi

say "Starting version $VERSION (the first time this downloads about 500 MB)"
bash deploy.sh "$VERSION"

say "Waiting for the POS to answer"
ok=0
for i in $(seq 1 90); do
  # Quiet while the POS is still starting; only the final result is shown.
  if curl -fs --max-time 5 -o /dev/null http://localhost/health; then ok=1; break; fi
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

  if [ "$LICENCE_ON" -eq 1 ] && [ -n "${POS_LICENCE_KEY:-}" ]; then
    say "Activating the licence"
    jar="$(mktemp)"
    curl -s -o /dev/null -c "$jar" -X POST http://localhost/api/auth/login -H 'Content-Type: application/json' -d "$body" || true
    answer="$(curl -s -b "$jar" -w ' [%{http_code}]' -X POST http://localhost/api/licence/activate -H 'Content-Type: application/json' -d "{\"key\":\"$(json_text "$POS_LICENCE_KEY")\"}" || true)"
    rm -f "$jar"
    case "$answer" in
      *'[200]') echo "Licence activated." ;;
      *) echo "The licence could not be activated now: $answer"; echo "Sign in as the owner and enter the key on the Licence page." ;;
    esac
  fi
fi

ip="$(hostname -I 2> /dev/null | awk '{print $1}' || true)"
say "The POS is running (version $VERSION)"
echo "On this PC:             http://localhost"
if [ -z "$ip" ]; then ip="<address of this PC>"; fi
echo "On tills and tablets:   http://$ip   (same network or Wi-Fi as this PC)"
if [ "$FIRST_TIME" -eq 1 ]; then echo "Sign in as the owner:   $POS_OWNER_EMAIL"; fi
if [ "$LICENCE_ON" -eq 1 ]; then
  licence_text="$(curl -fsS http://localhost/api/licence/status 2> /dev/null | grep -o '"message":"[^"]*"' | head -n 1 | cut -d'"' -f4 || true)"
  echo "Licence:                ${licence_text:-not known yet (sign in as the owner and open Licence)}"
fi
echo
echo "Tips: give this PC a fixed address in the router, so the link above never changes."
echo "      A backup is made every night at 03:00 and copied to a USB disk named POSBACKUP. Keep that disk plugged in."
echo "      Updates install themselves at night (03:30). To update now:  sudo bash shop-install.sh"
