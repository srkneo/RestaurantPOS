# Coding conventions (read by Aider on every task)

Follow these exactly. Copy the pattern of the named example file when one is given.

## Backend (ASP.NET Core 10, minimal APIs, EF Core + PostgreSQL)
- Code lives in `Modules/<Area>/` (Billing, Menu, Orders, Platform, Staff); shared code in `Shared/`. Namespace `POSSystem.<Area>`; entities in `POSSystem.Models`.
- Endpoints: a static class `<Thing>Endpoints` with `public static WebApplication Map<Thing>Endpoints(this WebApplication app)`, registered in `Program.cs` next to `app.MapBillEndpoints();`. Services are registered with `builder.Services.AddScoped<...>()` next to `AddScoped<BillService>()`.
  Example: `Modules/Billing/DayCloseEndpoints.cs`.
- Every endpoint has `.RequireAuthorization(p => p.RequireRole(...))`, `.WithName("PascalName")` and `.WithSummary("plain sentence")`.
  Roles: `admin` (owner), `manager`, `cashier`, `kitchen`, `platform` (super admin only).
- Errors: throw `ArgumentException` (400, `{ message }`), `KeyNotFoundException` (404), `OrderConflictException` (409); form errors use `Results.ValidationProblem` with lowercase keys (`billprefix`, `pin`). Catch them in a private `Run(...)` helper like DayCloseEndpoints.
- Request/response types are `record`s at the top of the service file (`CreateXRequest`, `XDto`).
- Entities with restaurant data inherit `TenantEntity` (TenantId, OutletId, CreatedAt are filled automatically). Always filter outlet data with `_db.CurrentOutletId`; the tenant filter is automatic.
- EF config goes in a new partial `Shared/AppDbContext.<Area>.cs` with `private void Configure<Area>(ModelBuilder modelBuilder)`, called in `AppDbContext.cs` after `ConfigureShifts(modelBuilder);`. Example: `Shared/AppDbContext.Shifts.cs`.
  Rules: money `HasPrecision(12, 2)`; enums `HasConversion<string>().HasMaxLength(n)`; foreign keys `DeleteBehavior.Restrict`; `Version` int as `IsConcurrencyToken()`; `HasQueryFilter(x => x.TenantId == CurrentTenantId)`; TenantId first in every index.
- Money is `decimal`, never `double`. Round with `BillCalculator.Paise(x)`. Never trust a price or total sent by the screen: read prices from the menu or the bill.
- Times are UTC (`DateTime.UtcNow`). The business day comes from `BillService.Today()`.
- New table or column: the task adds `setup: dotnet ef migrations add <Name>` and `allow: Migrations/`. Never edit old migrations.

## Backend tests (xUnit)
- One test class per feature in `RestaurantPOS.Tests/`, using `WebApplicationFactory<Program>` with an InMemory database and `TestAuthHandler`.
  Copy `Build()`, `As(f, role)`, `Json(...)` and `Read(...)` from `RestaurantPOS.Tests/DayCloseTests.cs`.
- Call the real HTTP API; check status codes and JSON fields (camelCase). Test the wrong role (403), bad input (400) and the happy path.

## Frontend (React + Vite, Bootstrap 5) in `ClientApp/ClientApp/src`
- Function components, `export default function Name()`. Hooks only inside the component, never at the top of the file.
- Text: `const t = useT();` from `../i18n/I18nContext`. Every visible text uses `t('area.key')`. New keys go in `scripts/i18n/<task>.json` as `{ "key": ["English", "ગુજરાતી"] }` and the task runs `setup: node scripts/i18n-add.mjs scripts/i18n/<task>.json`.
- API calls: `fetch(url)` for GET; `send(url, method, body)` from `../ui/forms` for POST/PUT. Form errors: `setErrors(await readErrors(res))`, shown with `<Field id label error hint>` from `../ui/forms`. Server messages: `(await res.json()).message`.
- States: `<StateMessage kind="loading" />`, `<StateMessage kind="error" onRetry={load} />` from `../ui/StateMessage`. Success: `const { showToast } = useToast();` from `../ui/ToastContext`.
- Layout classes: `<main className="pos-page">`, `<div className="pos-page-head"><h1>…</h1></div>`, cards `pos-card`, buttons `btn btn-primary px-4` / `btn btn-outline-secondary`. Styles in a `.css` file next to the component; no inline styles except a max width.
- Money on screen: `formatMoney(x)` from `../ui/format`. On receipts: `money(x)` and `printT(lang)` from `../print/printText`.
- Print pages open with `window.open(url, '_blank', 'noopener')`.
- Pages are wired in `AppRouter.jsx`: import, `<Route ... element={<RequireRole roles={...}>...}>`, and a `NavLink` when it belongs in the menu.
- Example page: `src/admin/BillingSettingsPage.jsx`. Example form with several states: `src/components/DayClosePage.jsx`.

## Frontend tests (Vitest + Testing Library)
- File `<Name>.test.jsx` next to the component, first line `// @vitest-environment jsdom`.
- Mock the API with `vi.stubGlobal('fetch', vi.fn(async (url, options) => ...))`; wrap in `<I18nProvider>` (plus `ToastProvider`, `MemoryRouter`, `AuthProvider` when the component needs them). Find elements by label, role or English text. Example: `src/components/DayClosePage.test.jsx`.

## Always
- Change only the files the task names. Keep existing code and comments. No new packages.
- Short comments that say why, not what.
