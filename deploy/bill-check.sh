#!/usr/bin/env bash
# Nightly money check (cron, 04:00 India time, after the backup). Re-adds every bill of the last 3 days and checks
# invoice numbers, tokens and payments. Sends a Telegram alert only when something is wrong (or the check itself fails).
set -euo pipefail
cd "$(dirname "$0")"
DB_CONTAINER="${DB_CONTAINER:-pos-db-1}"
DAYS="${DAYS:-3}"

env_value() { if [ -f .env ]; then grep -E "^$1=" .env | tail -n 1 | cut -d= -f2- || true; fi; }

notify() {
  local token chat
  token="$(env_value TELEGRAM_TOKEN)"; chat="$(env_value TELEGRAM_CHAT_ID)"
  if [ -n "$token" ] && [ -n "$chat" ]; then
    curl -sS --max-time 10 -X POST "https://api.telegram.org/bot${token}/sendMessage" \
      -d chat_id="$chat" --data-urlencode text="$1" > /dev/null || true
  fi
}
trap 'notify "❌ RestaurantPOS bill check could not run on $(hostname). See ~/pos/backups/bill-check.log"' ERR

# Each line: check name | number of problems. Void bills are skipped where their order may legally change after the void.
read -r -d '' SQL <<SQL || true
WITH recent AS (SELECT * FROM "Bills" WHERE "IssuedAt" > now() - interval '${DAYS} days')
SELECT 'bill total differs from its order lines', COUNT(*) FROM (
  SELECT b."Id" FROM recent b JOIN "OrderItems" oi ON oi."KitchenOrderId" = b."KitchenOrderId"
  WHERE b."Status" <> 'Void' GROUP BY b."Id", b."ItemsTotal" HAVING b."ItemsTotal" <> SUM(oi."Quantity" * oi."UnitPrice")) x
UNION ALL
SELECT 'bill lines do not add up to the bill', COUNT(*) FROM (
  SELECT b."Id" FROM recent b JOIN "BillLines" l ON l."BillId" = b."Id"
  GROUP BY b."Id", b."ItemsTotal", b."Cgst", b."Sgst", b."Discount", b."TaxableValue"
  HAVING b."ItemsTotal" <> SUM(l."Amount") OR b."Cgst" <> SUM(l."Cgst") OR b."Sgst" <> SUM(l."Sgst")
      OR b."Discount" <> SUM(l."Discount") OR b."TaxableValue" <> SUM(l."TaxableValue")) x
UNION ALL
SELECT 'total is not taxable + GST + round-off', COUNT(*) FROM recent
  WHERE "Total" <> "TaxableValue" + "Cgst" + "Sgst" + "RoundOff" OR abs("RoundOff") > 0.50 OR "Total" < 0
UNION ALL
SELECT 'order uses another restaurant''s menu item', COUNT(*) FROM "OrderItems" oi
  JOIN "KitchenOrders" o ON o."Id" = oi."KitchenOrderId" JOIN "MenuItems" m ON m."Id" = oi."MenuItemId"
  WHERE o."CreatedAt" > now() - interval '${DAYS} days' AND o."TenantId" <> m."TenantId"
UNION ALL
SELECT 'bill in another restaurant or outlet than its order', COUNT(*) FROM recent b JOIN "KitchenOrders" o ON o."Id" = b."KitchenOrderId"
  WHERE b."TenantId" <> o."TenantId" OR b."OutletId" <> o."OutletId"
UNION ALL
SELECT 'gap or repeat in invoice numbers', COUNT(*) FROM (
  SELECT 1 FROM "Bills" GROUP BY "TenantId", "OutletId", "FinancialYear"
  HAVING COUNT(*) <> MAX("Sequence") OR COUNT(DISTINCT "InvoiceNumber") <> COUNT(*)) x
UNION ALL
SELECT 'repeated token on the same day', COUNT(*) FROM (
  SELECT 1 FROM "KitchenOrders" WHERE "CreatedAt" > now() - interval '${DAYS} days'
  GROUP BY "TenantId", "OutletId", "BusinessDate", "TokenNumber" HAVING COUNT(*) > 1) x
UNION ALL
SELECT 'payment in another restaurant, or paid more than the total', COUNT(*) FROM (
  SELECT b."Id" FROM recent b JOIN "BillPayments" p ON p."BillId" = b."Id"
  GROUP BY b."Id", b."Total", b."TenantId" HAVING SUM(p."Amount") > b."Total" OR bool_or(p."TenantId" <> b."TenantId")) x
UNION ALL
SELECT 'paid bill not covered by its payments', COUNT(*) FROM (
  SELECT b."Id" FROM recent b LEFT JOIN "BillPayments" p ON p."BillId" = b."Id"
  WHERE b."Status" = 'Paid' GROUP BY b."Id", b."Total" HAVING COALESCE(SUM(p."Amount"), 0) <> b."Total") x
UNION ALL
SELECT 'bills checked', COUNT(*) FROM recent;
SQL

result="$(docker exec -i "$DB_CONTAINER" psql -U pos -d restaurant_pos -tA -F '|' -v ON_ERROR_STOP=1 -c "$SQL")"
problems="$(echo "$result" | grep -v '^bills checked|' | awk -F'|' '$2 > 0 { print "• " $1 ": " $2 }')"
checked="$(echo "$result" | grep '^bills checked|' | cut -d'|' -f2)"
echo "$(date -u +%FT%TZ) checked ${checked} bills from the last ${DAYS} days"

if [ -n "$problems" ]; then
  echo "$problems"
  notify "❌ RestaurantPOS bill check on $(hostname): problems in the last ${DAYS} days (${checked} bills checked)
${problems}
Run loadtest/check.sql on the server to see the bills."
  exit 1
fi
echo "bill check passed"
