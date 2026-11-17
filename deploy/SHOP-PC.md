# POS on a shop PC

The whole POS (tills, kitchen screen, database) runs on one small PC inside the restaurant.
Billing keeps working without internet, and the bills stay in the shop.

## What you need
- A mini PC with Ubuntu 24.04 (4 GB RAM or more, SSD), plugged into the shop's router with a cable.
- Internet for the setup and for updates only.
- Tills, tablets and the kitchen screen on the same network or Wi-Fi.

## Set up (about 10 minutes)
On the mini PC, open a terminal:
```
curl -fsSLO https://raw.githubusercontent.com/srkneo/RestaurantPOS/master/deploy/shop-install.sh
sudo bash shop-install.sh
```
It asks for the owner's email and a password, installs Docker, starts the POS and shows the link for the tills,
for example `http://192.168.1.20`. Sign in there as the owner and set up Store, Menu, Tables and Staff as usual.

In the router, give the mini PC a fixed address (DHCP reservation), so the link never changes.

## Update
```
sudo bash shop-install.sh 1.7.1
```
The database is backed up first (`/root/pos/backups`), settings are kept.

## Backups
- Every night at 03:00 a backup is written to `/root/pos/backups/nightly` (7 days are kept).
- Copy that folder to a USB disk regularly. A backup that sits only on the same PC does not survive a broken disk.

## Good to know
- The link is plain `http://` and works only inside the shop's network. Do not open the PC to the internet.
- Offline order saving in the browser is not needed here (the server is in the shop) and is off on `http://` links.
- Files and settings are in `/root/pos`. Logs: `cd /root/pos && docker compose --env-file .env --env-file version.env -f docker-compose.prod.yml logs --tail 50 api`
