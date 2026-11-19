# 1.9 browser test checklist (before releasing)

Test on the local POS (or WSL shop PC) as owner, cashier, waiter and kitchen. Tick each line.

## Menu
- [ ] Menu > item > Sizes & extras: add Half/Full and two extras; POS asks for them; kitchen screen and KOT show the extras; bill line shows size and extras.
- [ ] Menu > Kitchen stations: add Tandoor; put an item on it; kitchen screen station picker shows only its lines; KOT prints one ticket per station.
- [ ] Menu > category > Served from / until: set hours that exclude now; the POS hides the category; set hours that include now; it comes back.
- [ ] Menu > item > Combo: add 3 parts; order the combo; kitchen and KOT show "= 1 x ..."; bill has one line.

## Tables and orders
- [ ] Place an order on table 1, then pick table 1 again: hint says it adds to the token; KOT prints only the new round; kitchen shows "Round 2".
- [ ] Orders > Move table, Merge table, Split bill: each works and the bills add up.
- [ ] Staff > add a Captain (waiter): on a phone they can place orders and add rounds; no Bill buttons; billing calls are refused.
- [ ] Token screen (/tokens): tokens move from Preparing to Ready live.

## QR ordering
- [ ] Tables > QR codes: switch on; print preview of the sheet looks right.
- [ ] Scan a table QR from a phone on the same network: menu shows; order goes to the kitchen; second order adds a round.
- [ ] Call waiter / Ask for bill shows a message on the POS.
- [ ] Bill the table, then try a QR order: guest is told to ask the staff. New code: the old QR stops working.

## Bills, tips, expenses
- [ ] Bill > Add tip (cash and UPI): bill total unchanged; Day report shows tips per waiter; cash tip shows as cash in on the shift.
- [ ] Expenses page: from the till (needs an open shift) and from elsewhere; shift expected cash drops only for the till one.
- [ ] Day report: tips and expenses lines on the Z report; Share on WhatsApp opens WhatsApp with the summary.

## Reports
- [ ] Reports: Today / Last 7 days / This month; numbers match the Day report.
- [ ] Download sales (CSV) and GST bills (CSV) open in Excel.

## Getting started
- [ ] Setup checklist ticks match reality; "I printed a test bill" stays ticked after reload.
- [ ] Footer > Help in English and Gujarati.
- [ ] Platform > Demo data on an empty restaurant fills menu and three orders; a restaurant with orders has no Demo data button.

## POS and billing (later items)
- [ ] POS > Tables: tiles show free / busy with token and minutes / bill printed; tapping a tile picks the table.
- [ ] Billing settings > Cashier discount limit 10: as cashier a 20% discount asks for a manager PIN; wrong PIN refused; right PIN makes the bill; Audit log shows it; as manager no PIN asked.
- [ ] Bill > Send on WhatsApp: opens WhatsApp with items and total; with a customer phone it opens that chat.
- [ ] Billing settings > Manager PIN: turn off "Cashiers need a manager PIN to print a bill again"; as cashier reprint without a PIN works (reprint without a PIN); turn it back on and the PIN is asked again.

## Guests
- [ ] Store > Google review link: save a https link; printed bill shows the review QR; scanning opens the review page.
- [ ] Store > Feedback QR on: printed bill shows the feedback QR; scan from a phone; give 4 stars and a comment; second try says already given.
- [ ] Feedback page lists it with the average; turning Feedback QR off makes the old QR say the link does not work.
- [ ] With Feedback QR on, the WhatsApp bill message has the feedback link.
- [ ] Give 2 stars from a phone with the POS open on another screen: the POS shows the low rating message with the table and bill number.
- [ ] Day report > Share on WhatsApp: the summary has the guest rating line when the day has feedback.

## Accountant
- [ ] Reports > Download for Tally (XML): file opens; in Tally (if available) Import Data > Vouchers accepts it after creating the 8 ledgers; a credit note day has a Credit Note voucher.

## Shop PC (still from 1.7)
- [ ] Update now shows the "POS is restarting" screen, then the blue reload bar.
