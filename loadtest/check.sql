-- Run on the TEST server after a load test (never needed on production; the nightly check covers that).
-- Every query lists problems; an empty result means all good.
\echo '1. Bills whose items total is not the sum of their order lines'
SELECT b."Id", b."InvoiceNumber", b."ItemsTotal", SUM(oi."Quantity" * oi."UnitPrice") AS order_total
FROM "Bills" b JOIN "OrderItems" oi ON oi."KitchenOrderId" = b."KitchenOrderId"
GROUP BY b."Id" HAVING b."ItemsTotal" <> SUM(oi."Quantity" * oi."UnitPrice");

\echo '2. Bill lines that do not add up to the bill'
SELECT b."Id", b."InvoiceNumber" FROM "Bills" b JOIN "BillLines" l ON l."BillId" = b."Id"
GROUP BY b."Id" HAVING b."ItemsTotal" <> SUM(l."Amount") OR b."Cgst" <> SUM(l."Cgst") OR b."Sgst" <> SUM(l."Sgst")
   OR b."Discount" <> SUM(l."Discount") OR b."TaxableValue" <> SUM(l."TaxableValue");

\echo '3. Totals that are not taxable + GST + round-off, or a round-off over 50 paise'
SELECT "Id", "InvoiceNumber" FROM "Bills"
WHERE "Total" <> "TaxableValue" + "Cgst" + "Sgst" + "RoundOff" OR abs("RoundOff") > 0.50 OR "Total" < 0;

\echo '4. Order lines priced differently from the menu (no price changes happen during a load test), or using another restaurant''s menu item'
SELECT oi."Id", o."TenantId" AS order_tenant, m."TenantId" AS item_tenant, oi."UnitPrice", m."Price"
FROM "OrderItems" oi JOIN "KitchenOrders" o ON o."Id" = oi."KitchenOrderId" JOIN "MenuItems" m ON m."Id" = oi."MenuItemId"
WHERE o."TenantId" <> m."TenantId" OR oi."UnitPrice" <> m."Price";

\echo '5. Bills in a different restaurant or outlet than their order'
SELECT b."Id", b."TenantId", o."TenantId", b."OutletId", o."OutletId" FROM "Bills" b JOIN "KitchenOrders" o ON o."Id" = b."KitchenOrderId"
WHERE b."TenantId" <> o."TenantId" OR b."OutletId" <> o."OutletId";

\echo '6. Gaps or repeats in invoice numbers'
SELECT "TenantId", "OutletId", "FinancialYear", COUNT(*) AS bills, MAX("Sequence") AS highest, COUNT(DISTINCT "InvoiceNumber") AS distinct_numbers
FROM "Bills" GROUP BY "TenantId", "OutletId", "FinancialYear"
HAVING COUNT(*) <> MAX("Sequence") OR COUNT(DISTINCT "InvoiceNumber") <> COUNT(*);

\echo '7. Repeated tokens on the same day'
SELECT "TenantId", "OutletId", "BusinessDate", "TokenNumber", COUNT(*) FROM "KitchenOrders"
GROUP BY "TenantId", "OutletId", "BusinessDate", "TokenNumber" HAVING COUNT(*) > 1;

\echo '8. Payments in another restaurant than their bill, or paid more than the total'
SELECT b."Id", b."InvoiceNumber", b."Total", SUM(p."Amount") AS paid FROM "Bills" b JOIN "BillPayments" p ON p."BillId" = b."Id"
GROUP BY b."Id" HAVING SUM(p."Amount") > b."Total" OR bool_or(p."TenantId" <> b."TenantId");

\echo '9. Paid bills whose payments do not cover the total'
SELECT b."Id", b."InvoiceNumber" FROM "Bills" b LEFT JOIN "BillPayments" p ON p."BillId" = b."Id"
WHERE b."Status" = 'Paid' GROUP BY b."Id" HAVING COALESCE(SUM(p."Amount"), 0) <> b."Total";

\echo 'Summary'
SELECT COUNT(*) AS bills, COUNT(DISTINCT "TenantId") AS restaurants, SUM("Total") AS money FROM "Bills";
