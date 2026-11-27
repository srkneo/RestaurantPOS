# 11. Platform owner guide (Mahesh)

Only for the platform owner account. Sign in with the platform email; when the server has a Telegram bot set up, a **6-digit code** is sent to your Telegram and you type it to finish signing in (5 minutes, 5 tries).

## Sign-in security – Platform menu → Security

- **Authenticator app**: turn it on so sign-in works even when Telegram is down. Save the 10 backup codes somewhere other than your phone.
- **Change password**: the platform account may use a simple password of **8 or more characters** (no capitals, digits or symbols needed), because sign-in always needs the second step. Other devices are signed out.
- If Telegram is down and no app is set up: see "Platform sign-in code does not arrive" in `deploy/RUNBOOK.md`.

## Link a shop PC to a cloud restaurant

On **Platform → Licences**, open a licence and choose the cloud restaurant. That shop PC then takes its menu and store settings from the cloud every 5 minutes (see page 10).

## Platform – all restaurants

- List of restaurants with status, outlets, staff, last order and **Subscription** (trial / active / overdue / stopped / free use). Search and pages.
- **Add restaurant**: restaurant and outlet name, owner name, email and password. It gets 10 tables.
- **Demo data**: fills an empty restaurant with a sample menu and orders for demos. For a fresh demo, add a new restaurant and fill it.
- **Disable / Enable**: a disabled restaurant's staff cannot sign in.
- **Payment**: record a UPI or bank payment (monthly ₹1,000 / yearly ₹10,000, reference). The paid-up date moves forward. You can also set plan and paid-up date by hand.

### How subscriptions work

- Self-signup gives a **30-day trial**.
- After the paid-up date: 7 days **overdue** (warning banner only), then **stopped**: no new orders or bills, everything still viewable.
- Every day at **10 AM** Telegram lists restaurants whose trial/plan ends within 3 days, unpaid ones and ones stopped that day (needs the Telegram settings on the server).
- Restaurants created before subscriptions are **free use**.

> 📷 Screenshot: Platform page with Subscription column and Payment dialog
> 

## Licences – shop PC licences

Issue, renew or stop licences for shop PCs by device code; search and pages.

**Day code (v2.3.3):** the **Day code** button on a licence shows today's support code for that shop PC (works only today, India time, and only on that PC; the button is off for a licence without a PC or a stopped one). Read it out or type it yourself on the shop PC's sign-in page under **Support: sign in with today's code**. It opens a 2-hour support session there (payments, refunds, staff and passwords blocked; audit-logged), even when the PC is offline. How it works: each renewal puts a support secret, made from the licence private key, the licence and the PC, into the signed lease; the code is an HMAC of that secret with the PC and the date. Leases signed before v2.3.3 stay valid but have no secret, so a PC needs one renewal after the update. Endpoints: GET /api/platform/licences/{id}/support-code (platform only) and POST /api/auth/support-day-login (shop PC).

## Support

Your company name (Vyom Solutions), phone, WhatsApp, email, support hours and website. Every restaurant sees these on About/Help and the pricing page; shop PCs get them at their next licence renewal.

The product name shown on About is **AllInOne POS** until you save another one here. If you saved "RestaurantPOS" earlier, change Product name to AllInOne POS and save; the sign-in page, browser tab and installed app already say AllInOne POS (v2.4).

## Releasing a new version (from your phone)

- Claude writes task files; the **POS queue** on your PC builds, tests and merges them.
- To release: say **"release"** – Claude drops release.req into E:\Projects\tasks\queue. GitHub builds and deploys in about 5 minutes; Telegram confirms.
- To go back: rollback.req with a version (e.g. 1.9.6) redeploys that version.
- Once real shops are live, create E:\Projects\tasks\peak-guard.on: releases then wait until after 3 PM / 11 PM India time (write "now" in release.req to override).

## Server jobs to remember

- Add TELEGRAM_TOKEN and TELEGRAM_CHAT_ID to ~/pos/.env on the server and run ./[deploy.sh](http://deploy.sh) <version> to switch on the sign-in code and the 10 AM reminders.
- Nightly bill check re-adds the day's bills and alerts on any mismatch.
- Test logins are on the page "AWS server – restaurants and logins".
