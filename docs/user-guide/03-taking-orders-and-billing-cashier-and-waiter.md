# 3. Taking orders and billing (cashier and waiter)

Open **POS**. The left side shows the menu (categories, search, dishes); the right side is the current order.

> 📷 Screenshot: POS screen
> 

## Taking a dine-in order

1. Order type: **Dine-in**.
2. Tap the **Table** button. The table grid opens:
    - **Free** – nobody there.
    - **Token 12 · 20 min** – occupied, order open for 20 minutes.
    - **Bill printed** – bill printed, not paid yet.
    - **Paid · 15 min** with a **Customer left** button – paid, guests still sitting.
    - **Reserved 20:30 · Patel** – booked in the next two hours.
3. Tap a table. Tap dishes to add them; use + / − for quantity. A line can have a note such as "Jain" or "less spicy". Dishes with sizes/extras open a small chooser.
4. Tap **Place order**. You get a **token number**; the kitchen screen shows it at once. Tap **Print KOT** for a kitchen slip.

> 📷 Screenshot: table grid with Free / Token / Bill printed / Paid / Reserved
> 

## More items for the same table (running tab)

Choose the same table: the screen says "Table 5 has token 12 open. New items are added to it." Add dishes and place – they go to the kitchen as the next round on the **same token**.

If the bill was already printed but not paid, the new items still join the **same token and the same bill**; the bill total is worked out again. **Print the bill again** before taking payment. Once any money is received the bill is fixed and more items need a new order. Guests ordering from the table QR are asked to call staff in this case.

## Takeaway

Order type **Takeaway**, enter the customer name (and phone if your settings ask), add dishes, place. Tell the customer the token number; it appears on the token screen when ready.

## Making the bill

1. From the POS or the Orders page open the order and tap **Bill**.
2. Discount (optional): amount or %. Above the cashier limit a manager enters their **PIN**.
3. Tap **Make bill** – you get a numbered GST bill. **Print** it.

## Taking payment

1. Choose **Cash**, **UPI** or **Card**.
2. **Cash**: type the cash received; the screen shows the change to give.
3. **UPI**: the guest scans the QR, which already has the exact amount. Optionally type the UPI reference.
4. **Card**: type the amount and optional reference.
5. A bill can be paid in parts (e.g. part cash, part UPI). It becomes **Paid** when the parts reach the total.
6. A **tip** can be recorded separately (it is not part of the bill or GST).

> 📷 Screenshot: bill dialog with payment methods and UPI QR
> 

## Guest a few rupees short? (waive small change)

Example: bill ₹398, guest has ₹395 and you have no coins. Record **Cash ₹395**. A button **Waive ₹3.00 small change** appears; tap it and the bill is Paid. The printed bill and GST do not change; the ₹3 shows as "Small change waived" in reports. The owner sets the limit (default ₹5) in Admin ▾ → Billing.

## Void or reprint

- **Void**: a wrong bill. Give a reason; a cashier needs a manager PIN. Void bills keep their number and show as void – nothing is ever deleted.
- **Reprint**: the second print says DUPLICATE. Depending on settings it needs a PIN.

## After the food: Food delivered / Handed over

- Dine-in: when the food reaches the table tap **Food delivered**. The table stays busy until the bill is paid.
- Takeaway: tap **Handed over** when the customer collects.

## Guests leave: Customer left

After payment the table shows **Paid**. Guests often stay and chat or order again. When they leave, open the table grid and tap **Customer left** – the table is free again.

## Other actions (Orders page or order menu)

- **Edit or cancel** an order before the kitchen starts cooking (cancel needs a reason).
- **Move table**: guests changed tables.
- **Merge**: two tables become one bill. **Split**: some items move to a new token billed separately.

## Waiters

Waiters sign in on their phone, open **POS**, pick the table and place orders exactly as above. They cannot make bills or take payment.
