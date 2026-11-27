# Supermarket (DMart style): features and needs

Written 27 Nov 2026. DMart does not publish how its POS works; this list comes from how DMart runs its stores (low prices below MRP, savings shown on the bill, offer items marked "D", fast counters, stock rarely out) and from what supermarket POS systems in India offer. It extends grocery R1 and the phases in [architecture.md](architecture.md).

## 1. Fast checkout (phase 6)

- Scan-first billing screen, works with the keyboard only; a scanner reads 40+ items a minute, so the screen must never slow it down.
- Scan the same item again adds one; a quantity key (for example 6 x scan); repeat last line; remove last line.
- Hold / park a bill and resume it; several counters billing at the same time, each with its own bill number series.
- Carry bag as a chargeable item on one key.
- Payments: cash, UPI (dynamic QR with the amount), card, split payment; cash drawer opens from the printer.
- Customer-facing display showing each line, the total and "You saved".
- Bill: MRP, our price and saving on each line, "D" mark on offer items, "You saved X", GST summary by HSN.
- Optional customer phone number; e-bill by WhatsApp or SMS.

## 2. Loose items and weighing (phase 6 basic, phase 9 scales)

- PLU codes for fruit, vegetables, grains, dry fruits (short numbers typed at the counter).
- Barcode label scales: the weighing section prints a label whose barcode carries the item and the weight or price (EAN-13 starting with 2); the counter only scans it.
- Pre-weighed and labelled packs for fast movers before the rush.
- Price changes pushed to every counter and every scale.

## 3. Pricing and offers (phases 6 and 9)

- See "Pricing, offers and cashier discounts" in [architecture.md](architecture.md): price never above MRP, owner-set price rules, offers engine, cashier discounts from a fixed list with approval and log.
- Shelf label (price tag) printing with MRP, our price and saving; reprint all labels whose price changed today.

## 4. Stock (phase 7, auto-replenish in phase 9)

- Purchase order to supplier, goods receipt (GRN) checked against the PO, supplier bill entry.
- Stock goes down on every sale; stock take with a handheld scanner by aisle; differences need a reason.
- Damage, wastage and expiry write-offs with reasons.
- Batch and expiry: sell oldest expiry first; near-expiry list and markdown price.
- Auto replenishment: minimum and maximum per item and store, reorder suggestions from sales speed, so the shelf is rarely empty.
- Stock transfers between stores and from a warehouse.

## 5. Returns and exchange (phase 7)

- Return against the bill number (scan the bill), reason, credit note or refund, manager approval above a limit; returned stock goes back or to damage.

## 6. Counter control and loss prevention (phase 6)

- Cashier shift: open with cash float, close with a cash count by notes and coins; X report (mid-shift) and Z report (end of shift); difference shown to the manager.
- Void line, void bill and price override need a supervisor PIN; all logged.
- Reports per cashier: voids, discounts, returns, no-sale drawer opens.

## 7. Many stores (phase 9)

- Head office keeps the product and price master and pushes it to all stores (and their counters).
- Central purchasing and a warehouse (distribution centre) that supplies the stores.
- Store-wise and company-wide reports.

## 8. Reports and accounts (phase 4 onwards, grocery in phase 6+)

- Sales by hour, counter, cashier, category and brand; margin by category; fast and slow movers; stock value and ageing; near-expiry.
- GST: HSN summary, GSTR-1 export; Tally export.

## 9. Hardware

- Per counter: PC or tablet, barcode scanner, thermal printer, cash drawer, customer display, UPI QR or card machine.
- Store: barcode label scale(s), label printer for shelf tags and own packs, handheld scanner for stock take, UPS.

## 10. Later

- Online orders picked from the store (like DMart Ready), self-checkout, loyalty (DMart has none; our loyalty module can be switched on per client).
