# RestaurantPOS 1.9 – what is new

## Menu / મેનૂ
- **Sizes and extras**: an item can have sizes (Half, Full, Large) and extras (cheese, butter). The POS asks for them; the bill shows "Pizza (Large) + Extra cheese".
- **Kitchen stations**: put items on stations like Tandoor or Bar. Each kitchen screen can show one station, and one KOT prints per station.
- **Time-based menu**: a category can be served only at set hours (breakfast 07:00–11:00, late night 22:00–02:00). Outside them the POS hides it.
- **Combos**: a combo item lists what it includes. The bill shows one line at the combo price; the kitchen sees every part.

## Tables and orders / ટેબલ અને ઓર્ડર
- **Running tab**: choosing a table that already has an order adds the new items to it as the next round. Only the new round goes to the kitchen and on the KOT.
- **Table status**: the table list shows free, occupied (with token) or bill printed.
- **Move, merge, split**: move guests to a free table, join two tables into one bill, or split some items to a bill of their own.
- **Captain (waiter) role**: waiters take orders and add rounds from a phone; they cannot bill, cancel, merge or split.
- **Token screen**: a TV near the counter shows tokens being prepared and ready to collect.

## QR ordering at the table / ટેબલ પર QR થી ઓર્ડર
- Each table gets its own secret QR code. Guests scan it to see the menu, order, call a waiter or ask for the bill.
- Orders go straight to the kitchen, onto that table's running tab. A table with a printed bill takes no more QR orders.
- Off until the owner turns it on (Tables > QR codes). Print the QR sheet; a misused code can be replaced with a new one.

## Bills, tips and expenses / બિલ, ટિપ અને ખર્ચ
- **Tips**: add a tip on the bill for the waiter (or a shared pool). Tips are not in the bill total or GST; cash tips count in the till shift.
- **Expenses**: record money spent (milk, vegetables, gas, rent). Paid from the till comes out of the expected cash.
- **Z report** shows tips and expenses on separate lines, outside sales.

## Reports / રિપોર્ટ
- **Reports page**: sales for any range of days by day, category, item, payment, hour and staff, with GST per rate and expenses.
- **Downloads**: sales as CSV (opens in Excel) and the GST bill list as CSV for the accountant.
- **WhatsApp summary**: the Day report can send the day's summary on WhatsApp in one tap.

## Getting started / શરૂઆત
- **Setup checklist** for new owners: store details, billing and UPI, menu, tables, staff, printer test, QR ordering.
- **Help pages** in English and Gujarati (footer > Help).
- **Demo data** (platform owner): fill an empty restaurant with a sample Gujarati menu and three orders, for demos.

## POS and billing / POS અને બિલ
- **Table grid**: the POS has a Tables button showing every table as a tile: free, busy (token and minutes) or bill printed. Tap one to pick it.
- **Cashier discount limit** (Billing settings): the most discount a cashier gives alone. Above it a manager types their PIN on the bill; it is noted in the audit log. Empty = no limit.
- **Send on WhatsApp**: the bill opens WhatsApp with its items, total and due amount, straight to the customer's number when the order has one.
- **Reprint without a PIN** (Billing settings > Manager PIN): by default a cashier needs a manager PIN to print a bill again; the owner can turn that off. Void and credit notes always need a manager.

## Guests / ગ્રાહકો
- **Google review QR** (Store page): paste your Google review link and every bill prints a "Rate us on Google" QR code.
- **Feedback QR** (Store page): bills print a "Tell us how we did" QR. Guests give 1–5 stars and a comment without an app or sign-in, once per bill. Read them under Feedback, with the average. The WhatsApp bill carries the same link.
- **Low rating alert**: when a guest gives 1 or 2 stars, the POS shows a message with the table and bill number, so staff can check on them while they are still there.
- **Rating in the day summary**: the WhatsApp day summary adds "Guest rating 4.3 of 5 (12 guests)" for the day's bills.

## Accountant / એકાઉન્ટન્ટ
- **Tally export** (Reports): one Sales voucher per day (cash, UPI, card and unpaid against sales, CGST, SGST, round off) and one Credit Note voucher per credit note, for Tally Import Data. Ledgers: Cash, UPI Collections, Card Collections, POS Unpaid Bills, Sales, Output CGST, Output SGST, Round Off.

## 1.9.1 – fixes / સુધારા
- **Orders close properly**: paying the bill of a ready order closes it and frees its table. Ready orders on Orders have **Served** (dine-in) or **Collected** (takeaway). The token screen drops a ready token after 15 minutes at the latest.
- **Old open orders no longer hold a table**: an order left open on an earlier day does not block the table, so a new POS or QR order starts fresh and reaches the kitchen screen.
- **Guest QR menu**: a bar at the top with a dish search and category buttons that jump to that part of the menu.
- **Delete a table**: only a table that never had an order can be deleted; others can be marked not in use.
- **Search and pages**: Orders (token, name, table or item), Feedback (comment, bill number, rating), Menu (name, category, sold out), Platform restaurants and Shop licences.
- **About**: lists the free open-source software inside the POS and its licences.
- **Sample data script** (for testing): `scripts\seed-aws.ps1` fills a restaurant with a menu, staff and a busy day of orders.

## Next update
- **Settings saved at the same moment** on two screens no longer undo each other.
- **Hindi** (हिन्दी): a third language button. Daily screens are in Hindi; owner pages still show English for now.
