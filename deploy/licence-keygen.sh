#!/usr/bin/env bash
# One time only: makes the key pair that signs shop licences. Run it on your own computer (Ubuntu or WSL), never on a shop PC.
#   bash deploy/licence-keygen.sh
# The PRIVATE key goes only into the cloud server's .env (as LICENCE_PRIVATE_KEY) and your password manager. Never commit it.
# The PUBLIC key is committed as deploy/licence-public.key; shop PCs use it to check licences.
# If you make new keys later, every licence signed with the old key stops working.
set -euo pipefail

OUT="${1:-$HOME/licence-keys}"
if [ -e "$OUT/licence-private.b64" ]; then
  echo "Keys already exist in $OUT. Move them away first if you really want new ones."
  exit 1
fi
command -v openssl >/dev/null || { echo "openssl is needed:  sudo apt install openssl"; exit 1; }

mkdir -p "$OUT"
chmod 700 "$OUT"
openssl ecparam -name prime256v1 -genkey -noout -out "$OUT/key.pem"
openssl pkcs8 -topk8 -nocrypt -in "$OUT/key.pem" -outform DER | base64 -w0 > "$OUT/licence-private.b64"
openssl ec -in "$OUT/key.pem" -pubout -outform DER 2>/dev/null | base64 -w0 > "$OUT/licence-public.b64"
rm -f "$OUT/key.pem"
chmod 600 "$OUT/licence-private.b64"

echo
echo "Done. Files in $OUT:"
echo "  licence-private.b64   SECRET. Only for the cloud server .env and your password manager."
echo "  licence-public.b64    Safe to share. Copy it to deploy/licence-public.key in the repo and commit it."
