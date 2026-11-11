\# Restaurant POS \& Kitchen Display System (KDS)



A real-time restaurant point of sale and kitchen display system.



\## Tech Stack

\- \*\*Backend:\*\* ASP.NET Core 10, Entity Framework Core, SignalR, NLog

\- \*\*Frontend:\*\* React (Vite)

\- \*\*Database:\*\* PostgreSQL (Docker, port 5433)

\- \*\*Tests:\*\* xUnit, Moq, EF Core InMemory

\- \*\*AI pair programmer:\*\* Aider with local Ollama `qwen2.5-coder:14b`



\## v1.0 (released)

\- POS: pick a table, add items to the cart, place an order, get a daily token number

\- KDS: new orders appear live over SignalR (no refresh)

\- Mark Ready on the KDS, and the POS shows an "Order X is Ready" banner



\## Roadmap

\- \*\*v1.1\*\* Data foundation: order items with quantities, status enum, EF migrations, tables API, tenant/outlet columns, CI

\- \*\*v1.2\*\* Security: login (Identity + JWT), roles (admin, manager, cashier, kitchen)

\- \*\*v1.3\*\* Core POS: menu admin, tax and discounts, billing, payments

\- \*\*v1.4\*\* Kitchen and operations: stations, timers, QR table ordering

\- \*\*v1.5\*\* Reporting and shifts

\- \*\*v2.0\*\* Multi-outlet and microservices



\## Daily startup

1\. Database: `docker start pos-postgres`

2\. Local LLM: `ollama serve`

3\. Backend (project root): `dotnet run`

4\. Frontend: `cd ClientApp\\ClientApp` then `npm run dev`

5\. Aider: `python -m aider --model ollama/qwen2.5-coder:14b --map-tokens 0 --edit-format diff`



\## Tests

```powershell

dotnet test RestaurantPOS.Tests

```

