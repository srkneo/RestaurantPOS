# Shop PC: setup visit checklist

One page for setting up the POS on a shop's own mini PC. Print it and tick as you go.

## At home, before the visit (about 1 hour)
- [ ] Live site, **Licences** page: create a licence for the restaurant (trial). Write down the key.
- [ ] Mini PC firmware (press Del or F2 at start): set **Restore on AC power loss = Power On**, so the PC starts by itself after a power cut.
- [ ] Install **Ubuntu Server 24.04 LTS** (or Desktop), user name of your choice, and run `sudo apt update && sudo apt upgrade -y`.
- [ ] With the PC on your internet: `curl -fsSLO https://raw.githubusercontent.com/srkneo/RestaurantPOS/master/deploy/shop-install.sh` and `sudo bash shop-install.sh`. Use the owner's email and a first password; enter the licence key when asked.
- [ ] Open `http://<PC address>` from your laptop: sign in as the owner, Licence page shows **Active**.
- [ ] Optional: menu scan from photos of the paper menu, so the menu is ready before you arrive.
- [ ] Format a USB disk as exFAT named **POSBACKUP** (Windows: right-click the drive, Format, Volume label). Take two if the owner wants to swap them weekly.

## At the shop (about 1 hour)
1. [ ] Put the PC next to the router, both plugged into the **UPS**. Connect the PC to the router with a network cable.
2. [ ] Router admin page: give the PC a **fixed address** (DHCP reservation for its MAC address). Restart the PC and check the address stays the same.
3. [ ] Plug the **POSBACKUP** disk into the PC.
4. [ ] On every till, tablet and the kitchen screen: open `http://<PC address>`, add it to the home screen. Kitchen screen: sign in with its device account (Staff page, kitchen screen device).
5. [ ] Owner screens, with the owner:
   - **Store**: name, address, phone, GSTIN, FSSAI, logo, bill footer.
   - **Billing**: GST mode, prices with or without GST, UPI ID for the QR, paper width (58 or 80 mm), bill language.
   - **Menu** (check prices and GST rates), **Tables**, **Staff** (cashier and manager accounts, manager PINs), **Settings** (order types, tokens).
6. [ ] Printer: print a test bill and a KOT from the browser (paper width and margins right).
7. [ ] Full test: place an order, kitchen marks it ready, make the bill, pay by UPI QR, print. Then **Day close** with a cash count.
8. [ ] Backups: **Licence** page, **Back up now**. After 2 minutes "Last copy off this PC" shows the time.
9. [ ] Power cut test (with the owner watching): switch the PC off at the wall, switch on again. Within about 3 minutes the POS answers again on the tills, and the test bill is still there.
10. [ ] Delete the test order and bill: void the bill with the reason "Setup test". (Bills are never deleted; a void keeps the numbering honest.)

## Tell the owner
- Support: the number and hours on the **About** page (the **?** at the top). **Copy details** there and send them on WhatsApp when asking for help.
- Keep the PC, router and UPS switched on, day and night. Updates and backups run at night.
- Keep the **POSBACKUP** disk plugged in. A yellow bar appears when no copy was made for 2 days.
- Billing works without internet for 7 days. After that the licence needs a moment of internet to renew.
- Forgot the owner password: Sign in page, **Forgot password?**, and call support for a one-time code.
- Do not use this PC for anything else (no browsing, no other software).

## Write down for your records
| | |
|---|---|
| Restaurant and owner phone | |
| Licence key | |
| PC code (About page) | |
| PC address (fixed) | |
| Router admin address and password location | |
| Mini PC model, bought on | |
| UPS model, bought on | |

## After the visit
- [ ] Live site, **Licences**: the shop shows today as last contact and the right version.
- [ ] Next morning: the shop's Licence page shows a backup copy from 03:00.
