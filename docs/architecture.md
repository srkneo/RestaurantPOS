# Architecture blueprint: AllInOne POS

Agreed 26 Nov 2026. The product grows from Restaurant POS into **AllInOne POS**: one system for restaurants, bakeries, dairies, grocery (kirana), supermarkets (D-Mart style), pharmacies and more, each set up per client from the platform admin.

## Principles

- **Modular monolith.** One app that runs the same in the cloud and on the offline shop PC. No microservices for billing.
- **Capabilities are the plugins**, not business types. Each capability is a module with its own backend, tables, screens and tests, built once and shared:
  tables and KDS, tokens, weight (sell by kg/litre), barcode, batch and expiry, khata (credit), purchase, promotions, prescription, multi-store.
- **An outlet type is a template**: a bundle of capabilities plus settings, custom fields and a bill template. Restaurant, bakery, dairy, grocery, pharmacy and supermarket are templates. Any template can be changed per client with no code change.
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

## Other outlet types on the roadmap

Tea, juice and ice-cream stalls, food court or canteen, Gujarati thali, farsan by weight, advance and party orders, phone and home delivery orders.
