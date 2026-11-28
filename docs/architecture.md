# Architecture blueprint: AllInOne POS

Agreed 26 Nov 2026. The product grows from Restaurant POS into **AllInOne POS**: one system for restaurants, bakeries, dairies, grocery (kirana), supermarkets (D-Mart style), pharmacies and more, each set up per client from the platform admin.

## Principles

- **Modular monolith.** One app that runs the same in the cloud and on the offline shop PC. No microservices for billing.
- **Capabilities are the plugins**, not business types. Each capability is a module with its own backend, tables, screens and tests, built once and shared:
  tables and KDS, tokens, weight (sell by kg/litre), barcode, batch and expiry, khata (credit), purchase, promotions, prescription, multi-store.
- **An outlet type is a template**: a bundle of capabilities plus settings, custom fields and a bill template. Restaurant, bakery, dairy, grocery, pharmacy and supermarket are templates. Any template can be changed per client with no code change.
- **Only the platform owner chooses the outlet type and features**, when creating the restaurant (pick a type for its default features, or tick features one by one). The restaurant owner cannot change them; they only set operational options (time zone, tokens, receipt, GST).
- **The licence and plan carry capabilities.** The server enforces them; the UI shows only what is switched on. Grocery is sold as a separate plan.

## Core domain

Catalog item (today's MenuItem), Sale (today's Order), Payment, Stock ledger (movements, not one quantity), Customer, Tax.

## Data

- **PostgreSQL is the source of truth**, one database with **one schema per module** (`core`, `restaurant`, `pharmacy`, ...).
- A module's fields live in its own tables, so a grocery store has no restaurant rows at all.
- **JSONB** custom fields for per-client extras, defined in the platform admin.
- Search: Postgres trigram first (barcode and name, tens of thousands of items). Meilisearch only if needed.
- Add only when needed: Redis cache, a read replica or analytics store for big reports, object storage for images.
- **Event outbox** table feeds integrations without touching billing code.
- Small clients share one database (tenant id, as today). A big chain can get its own database through a tenant-to-connection map.

## Extension points

- Backend module interface (services, endpoints, migrations, menu entries, feature flags).
- Capability registry: `Modules/Platform/Capabilities.cs` lists every capability (key, name, description, depends on). Keys are lowercase kebab-case (`tables`, `kitchen-screen`, `qr-ordering` ...); `Capabilities.Validate` checks a list (unknown key, listed twice, missing dependency) and `Capabilities.WithDependencies` adds what a list needs. Outlet types (C2), the per-restaurant list (C3), plans (C4), the shop PC lease (C5) and the RequireCapability filter (C6) all use these keys.
- Outlet types: `Modules/Platform/OutletTypes.cs` lists the types the platform gives a business (restaurant, cafe, takeaway-counter, cloud-kitchen, bakery, dairy). Each type = capability keys (registry order, dependencies included) + a settings preset (`OutletTypes.ApplyPreset`: dine-in on/off, default order type, takeaway needs a name, kitchen screen, simple counter; other settings kept). The presets match today's owner presets (`OutletSettings.WithPreset`, restaurant = dine-in) until C9c moves the choice to the platform and removes the owner buttons.
- Audit log: `AppDbContext.Audit.cs` writes `AuditLogs` (Details is JSON, a jsonb column on PostgreSQL). `Modules/Platform/AuditLogQuery.cs` reads it for GET /api/admin/audit (owner, `?take=1..500&type=<EntityType>`) and GET /api/platform/tenants/{id}/audit (platform); each row has `support` = true for support sign-ins and changes made by a support user. Screen: `admin/ActivityLogPage.jsx` (/admin/activity and /platform/tenants/:id/activity).
- Frontend route and menu registry, lazy loaded per module.
- Hardware layer: printer, barcode scanner, weighing scale, cash drawer.
- Connectors: outbox, webhooks and a connector API for Tally, Swiggy/Zomato, GST e-invoice and other POS systems.
- Only these may later split into services: cloud sync and licence, reports, integrations worker, search.

## Plan

Each phase is small Aider steps. Claude writes the tests that prove nothing broke.

| # | Phase | Risk |
|---|---|---|
| 0 | Finish bakery (BK1), sell by weight (WT1, WT2), release 2.3.2 | low |
| 1 | Rename visible names to AllInOne POS (app title, bills, licence text, guides, i18n). Repo, folder and Docker names later, at a quiet time. Code namespaces stay. | low |
| 2 | Capability registry, licence capabilities, menus shown only for enabled capabilities | low |
| 3 | Move code into module folders, one Postgres schema each | medium |
| 4 | Generalise the core: MenuItem to Item, Order to Sale, restaurant fields into the restaurant module. Step by step, backup before each migration, upgrade tests on a copy of a real shop database. | high |
| 5 | Stock ledger, barcodes, search, custom fields | medium |
| 6 | Retail POS screen (scan first) and grocery template: grocery R1 | medium |
| 7 | Purchase entry, batch and expiry, khata (shared by grocery and pharmacy) | medium |
| 8 | Pharmacy module: batch and expiry required, Schedule H register, doctor and patient on the bill, drug licence on the invoice, search by salt | medium |
| 9 | Promotions, multi-store, central purchasing, D-Mart template | high |
| 10 | Connectors and webhooks | low |

Order chosen: grocery before pharmacy; khata in phase 7.

## Grocery / kirana requirements (summary)

- Products: names (English and Gujarati), several barcodes, short code, HSN, GST, MRP (selling price never above MRP), cost price, unit (piece, pack, kg, litre), Excel import.
- Billing screen: USB or Bluetooth scanner works as a keyboard; scan adds or adds one more; search by name, code or barcode; hold and resume a bill; line discount; cash, UPI or split; bill shows MRP, "You saved", HSN-wise GST summary.
- Stock: purchase entry, automatic decrease on sale, low-stock alerts, stock take with the scanner, batch and expiry.
- Khata: ledger per customer, payments, balance, WhatsApp reminder.
- Returns and exchange with a credit note.
- Reports: item-wise sales, margin, fast and slow movers, HSN GST summary, stock value, near-expiry.
- Later: label printing, offers, weighing scale and price-embedded barcodes.
- Hardware: barcode scanner, thermal printer, cash drawer; later a label printer and a scale.

## Shared product catalog

Decided 27 Nov 2026. Part of grocery R1 (phase 6). Owners should not type every product by hand.

- **One master catalog owned by the platform**, read by every shop: barcode (EAN-13), name (English and Gujarati), brand, pack size, unit, category, HSN, GST rate, **last known MRP** (with the date it was seen).
- **No selling prices in the catalog.** Selling price and the shop's own MRP live in each shop's product list. A new batch can carry a new MRP, so the shop confirms MRP at purchase entry.
- **Scan flow:** scan, then look in the shop's own products, then in the master catalog. If found, the form is pre-filled and the owner only types the price (and confirms MRP).
- **Starter pack:** a new shop picks categories (staples, snacks, soaps, ...) and imports them in one click, then fills prices in an Excel sheet.
- **Storage and speed:** even 500,000 products is about 100-150 MB in PostgreSQL with indexes. Barcode lookup by a unique index (under 1 ms); name search with pg_trgm (part words and spelling mistakes). Own `catalog` schema, no tenant id.
- **Shop PC (offline):** downloads a compressed copy (tens of MB) or only its chosen categories, so lookups work without internet.
- **Sources:**
  1. Open Food Facts (free, about 15,000 Indian food products in 2025). Open Database Licence: credit them and share fixes to their data back.
  2. GS1 India DataKart (official national product data, tens of millions of barcodes). Needs retailer registration; cost and API terms to be asked before the first grocery client.
  3. Our own shops: every new product an owner adds (barcode, name, brand, size, never prices) goes to a review list; the platform owner approves it and every shop gets it. Over time this is the biggest source.
- No product images at first; later thumbnails in object storage.

## Pricing, offers and cashier discounts

Decided 27 Nov 2026, following how DMart works: prices are set in advance, the cashier does not invent discounts.

- **Selling price is never above MRP** (law); the system blocks it.
- **Price rules, set by the owner or manager:**
  - per product: a fixed price, or MRP minus X%
  - per category or brand: for example all soaps at 8% off MRP
  - per date range: for example a Diwali offer
- **Offers engine (phase 9):** buy X get Y, combos, quantity price (3 for 99), bill-value offers (above 1,000 get an item free).
- **Cashier discounts from a fixed list only**, for example "Senior citizen 5%", "Damaged pack 10%", "Staff 10%". The owner sets each discount and which roles may use it; above its limit a manager must approve (the existing bill approval flow); every discount is logged with the cashier's name; no free typing of amounts.
- **Bill:** MRP, our price and the saving on each line, a "D" mark on offer items, and "You saved X" at the bottom.
- **GST:** prices include GST, as printed on the pack; the bill works out the tax from the price after discounts.

## Supermarket (DMart style)

The full feature list is in [supermarket-dmart-style.md](supermarket-dmart-style.md). Where it lands: fast checkout, PLU, shelf labels and cashier shifts in phase 6; purchase, GRN, stock take, batch and expiry, returns in phase 7; offers, barcode label scales, auto replenishment, many stores and a warehouse in phase 9.

## Other outlet types on the roadmap

Tea, juice and ice-cream stalls, food court or canteen, Gujarati thali, farsan by weight, advance and party orders, phone and home delivery orders.
