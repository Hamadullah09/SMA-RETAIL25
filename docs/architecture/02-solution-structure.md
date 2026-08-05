# 02 — Solution Structure

## Repository root

```
Retail25/
├─ docs/
│  └─ architecture/                     # this dossier
├─ backend/
│  ├─ Retail25.sln
│  ├─ Directory.Build.props             # nullable, warnings-as-errors, langversion, analyzers
│  ├─ Directory.Packages.props          # central package management (CPM)
│  ├─ .editorconfig
│  └─ src/ … tests/                     # see below
├─ frontend/
│  └─ retail25-web/                     # Next.js 14 App Router
├─ agent/                               # shipped with backend solution, deployed separately
├─ deploy/
│  ├─ docker-compose.yml                # sqlserver, redis, api, web, seq/otel-collector
│  ├─ docker-compose.prod.yml
│  ├─ Dockerfile.api  Dockerfile.web
│  ├─ nginx/ (or caddy/)                # TLS termination, single origin for API + web
│  └─ agent-installer/                  # MSI / winsw service wrapper for the terminal daemon
├─ tools/
│  ├─ rfid-simulator/                   # emits synthetic LLRP tag reports
│  └─ seed/                             # demo dataset (mirrors the legacy TST location)
├─ .github/workflows/ci.yml
└─ README.md
```

---

## Backend — Clean Architecture

Dependency rule, enforced by a NetArchTest test in `Retail25.ArchitectureTests`:

```
        ┌─────────────┐
        │   Domain    │  ← no project references, no NuGet except primitives
        └──────▲──────┘
               │
        ┌──────┴──────┐
        │ Application │  ← MediatR, FluentValidation. Defines interfaces (ports).
        └──────▲──────┘
               │
   ┌───────────┴────────────┐
   │      Infrastructure    │  ← EF Core, Redis, OpenIddict, adapters (payments, accounting…)
   └───────────▲────────────┘
               │
        ┌──────┴──────┐
        │     Api     │  ← composition root only
        └─────────────┘
```

`Contracts` is referenced by Api, TerminalAgent and (via generated TS) the frontend. It contains
**only** DTOs and hub method signatures — no logic.

```
backend/
├─ src/
│  ├─ Retail25.Domain/
│  │  ├─ Common/                      Entity, AggregateRoot, IDomainEvent, ValueObject, Result<T>
│  │  ├─ ValueObjects/                Money, Quantity, StockCode, Epc, Percentage, DateRange, Address
│  │  ├─ Catalog/
│  │  │  ├─ Product.cs                ProductType, pricing fields, tax flags, messages, links
│  │  │  ├─ ProductPrice.cs           4 price levels
│  │  │  ├─ PriceBreak.cs  SalePricing.cs  BonusPricing.cs
│  │  │  ├─ ProductVariant.cs  MatrixDimension.cs      (Matrix items)
│  │  │  ├─ KitComponent.cs
│  │  │  ├─ SerializedUnit.cs         serial number AND/OR RFID EPC
│  │  │  ├─ Department.cs  Category.cs  ProductSupplier.cs
│  │  │  └─ Events/                   ProductPriceChanged, StockLevelChanged…
│  │  ├─ Inventory/
│  │  │  ├─ StockLevel.cs             per product+variant+location snapshot (derived)
│  │  │  ├─ StockLedgerEntry.cs       append-only movement
│  │  │  ├─ StockTransfer.cs  StockCount.cs
│  │  │  └─ CostingPolicy.cs          moving-average, landed cost
│  │  ├─ Sales/
│  │  │  ├─ Cart.cs  CartLine.cs  CartAdjustment.cs  CartTaxOverride.cs
│  │  │  ├─ SalesTransaction.cs  SaleLine.cs  SaleTender.cs  SaleTaxSnapshot.cs
│  │  │  ├─ CustomerOrder.cs  Layaway.cs  PriceQuote.cs
│  │  │  ├─ GiftCertificate.cs
│  │  │  └─ Pricing/                  PricingContext, PriceResolution, IPricingRule (see doc 04)
│  │  ├─ Taxation/                    TaxConfiguration, TaxCalculator, TaxLineResult
│  │  ├─ Customers/                   Customer, CustomerAccount, CustomerPricingProfile, LoyaltyLedgerEntry
│  │  ├─ Receivables/                 Invoice, InvoicePayment, ARLedgerEntry, LateChargePolicy
│  │  ├─ Purchasing/                  Supplier, PurchaseOrder, PurchaseOrderLine, Receipt, OrderQuantityStrategies/
│  │  ├─ Staff/                       StaffProfile, TimeClockEntry, CommissionRule
│  │  ├─ Terminals/                   Station, DrawerSession, DrawerLedgerEntry, PrinterProfile, ReaderProfile
│  │  ├─ Identification/              IdentifierResolver, RandomWeightBarcodeParser, Code39
│  │  └─ Configuration/               Location, BusinessProfile, PosPolicy, LoyaltyPolicy, TenderType, Currency
│  │
│  ├─ Retail25.Application/
│  │  ├─ Abstractions/                ← the ports
│  │  │  ├─ IApplicationDbContext.cs  IUnitOfWork.cs  ICurrentUser.cs  IDateTime.cs
│  │  │  ├─ ICartStore.cs             Redis-backed server cart
│  │  │  ├─ ITagDebouncer.cs          RFID
│  │  │  ├─ IPosNotifier.cs           SignalR fan-out (Application never references SignalR)
│  │  │  ├─ IPaymentGateway.cs  IGiftCardProvider.cs
│  │  │  ├─ IAccountingConnector.cs   ISupplierPortal.cs
│  │  │  ├─ IDocumentRenderer.cs      IReceiptFormatter.cs  ILabelRenderer.cs
│  │  │  └─ IFileStorage.cs  IEmailSender.cs
│  │  ├─ Behaviors/                   Validation, Logging, Transaction, Idempotency, Authorization, Performance
│  │  ├─ Carts/  Sales/  Catalog/  Inventory/  Customers/  Receivables/
│  │  ├─ Purchasing/  Staff/  Reports/  Terminals/  Configuration/  Sync/  Migration/
│  │  │   └─ each: Commands/  Queries/  EventHandlers/  Dtos/  Validators/
│  │  └─ DependencyInjection.cs
│  │
│  ├─ Retail25.Infrastructure/
│  │  ├─ Persistence/
│  │  │  ├─ ApplicationDbContext.cs
│  │  │  ├─ Configurations/           one IEntityTypeConfiguration per entity
│  │  │  ├─ Migrations/               explicit EF Core migrations
│  │  │  ├─ Interceptors/             AuditingInterceptor, DomainEventDispatcher, OutboxInterceptor
│  │  │  ├─ Outbox/                   OutboxMessage, OutboxProcessor (BackgroundService)
│  │  │  └─ Seed/
│  │  ├─ Identity/                    ApplicationUser, OpenIddict seeding, PermissionPolicyProvider
│  │  ├─ Caching/                     RedisCartStore, RedisTagDebouncer, RedisLock
│  │  ├─ Realtime/                    SignalRPosNotifier (implements IPosNotifier)
│  │  ├─ Payments/                    SimulatorGateway + <Vendor>Gateway (Q1)
│  │  ├─ Accounting/                  QuickBooksOnlineConnector, GenericRestConnector, CsvExportConnector (Q2)
│  │  ├─ Documents/                   QuestPdf templates, EscPosReceiptFormatter, Code39Renderer, Avery layouts
│  │  ├─ Legacy/                      DbfReader (DBF + FPT memo), field maps for INV/CLIENT/INVOICE/SUPPLIER/TOTAL
│  │  ├─ Jobs/                        Hangfire: LateChargeAccrual, AccountingSync, FiscalYearClose, Retention
│  │  └─ DependencyInjection.cs
│  │
│  ├─ Retail25.Api/
│  │  ├─ Program.cs                   full pipeline (OpenIddict + PKCE, CORS, SignalR, health)
│  │  ├─ Endpoints/                   Minimal API groups, one file per module
│  │  ├─ Hubs/                        PosHub, InventoryHub, TerminalHub
│  │  ├─ Middleware/                  ExceptionHandler → ProblemDetails, RequestLogging, TenantResolution
│  │  ├─ Auth/                        PermissionAttribute, StepUpRequirement (supervisor override)
│  │  └─ appsettings*.json
│  │
│  ├─ Retail25.Contracts/             DTOs + hub interfaces, shared with agent; TS generated from here
│  │
│  ├─ Retail25.TerminalAgent/         .NET 8 Worker Service (see doc 06)
│  │  ├─ Program.cs                   Host + Serilog + auto-update hook
│  │  ├─ Services/
│  │  │  ├─ RfidReaderService.cs      IHostedService — LLRP session, keepalive, reconnect
│  │  │  ├─ TagDebounceService.cs     local ring buffer + server-side Redis debounce
│  │  │  ├─ ServerConnection.cs       SignalR client w/ exponential backoff + offline queue
│  │  │  ├─ CashDrawerService.cs      ESC/POS pulse 27,112,0,50,250
│  │  │  ├─ ScaleService.cs           System.IO.Ports, W/Z commands
│  │  │  ├─ PoleDisplayService.cs     PD3000 serial
│  │  │  ├─ ReceiptPrinterService.cs  raw ESC/POS, 20/40-col + cutter/red/black codes
│  │  │  └─ LocalApi.cs               http://127.0.0.1:8477 for browser-initiated actions
│  │  └─ appsettings.json             stationId, apiUrl, device profiles
│  │
│  └─ Retail25.Migration/             CLI: analyze | dry-run | import legacy DBF
│
└─ tests/
   ├─ Retail25.Domain.UnitTests/          pricing/tax golden files, EPC state machine
   ├─ Retail25.Application.UnitTests/     handlers with in-memory fakes
   ├─ Retail25.IntegrationTests/          Testcontainers: SQL Server + Redis, real HTTP
   ├─ Retail25.ArchitectureTests/         NetArchTest dependency rules
   └─ Retail25.LoadTests/                 k6/NBomber: 50 stations × bulk RFID
```

### Key backend package choices

| Concern | Package | Why |
|---|---|---|
| Mediation | `MediatR` | Brief mandates CQRS via MediatR |
| Validation | `FluentValidation` | Pipeline behavior, no attribute soup |
| Mapping | `Mapster` | Compile-time, faster than AutoMapper, no runtime config drift |
| ORM | `Microsoft.EntityFrameworkCore.SqlServer` 8 | Explicit migrations per brief |
| Auth server | `OpenIddict.Server.AspNetCore` 5.x | Brief mandate |
| Background jobs | `Hangfire.PostgreSql` | Durable retries + dashboard for late charges/sync/close |
| PDF | `QuestPDF` | Invoices, statements, POs, labels, price tags |
| Barcode | `ZXing.Net` (+ custom Code 39) | Removes the `3of9.ttf` install step |
| RFID | `LLRP.NET` / vendor SDK (Q3) | Behind `IRfidReader` |
| Excel | `ClosedXML` | Legacy "Open In MS-Excel" parity for exports |
| Logging/Tracing | `Serilog` + `OpenTelemetry` | Structured logs, traces to OTLP |
| Tests | `xUnit`, `FluentAssertions`, `Testcontainers`, `Verify`, `NetArchTest` | |

---

## Frontend — Next.js 14 App Router

```
frontend/retail25-web/
├─ src/
│  ├─ app/
│  │  ├─ (auth)/
│  │  │  ├─ login/page.tsx
│  │  │  └─ callback/route.ts             # PKCE code exchange, sets httpOnly cookies
│  │  ├─ (pos)/
│  │  │  └─ pos/
│  │  │     ├─ layout.tsx                 # chrome-less, fullscreen, keyboard capture
│  │  │     ├─ page.tsx                   # the 5-region POS (doc 08)
│  │  │     └─ _components/
│  │  │        ├─ CartPane.tsx  LiveRfidFeed.tsx  LineDetailDrawer.tsx
│  │  │        ├─ TotalsPanel.tsx  PaymentMatrix.tsx  SplitTenderDialog.tsx
│  │  │        ├─ CustomerContext.tsx  StatusBar.tsx  NumericPad.tsx
│  │  │        ├─ CreditsMenu.tsx         # F3: discount/coupon/return/gift cert/bottle/trade-in
│  │  │        ├─ DrawerMenu.tsx          # F10: float/view/print/close/pay in/out/pop
│  │  │        └─ SpecialMenu.tsx         # F11: unknown item/void/suspend/recall/taxes/output/staff
│  │  ├─ (back-office)/
│  │  │  ├─ layout.tsx                    # sidebar + command palette
│  │  │  ├─ inventory/  customers/  invoices/  suppliers/  purchase-orders/
│  │  │  ├─ staff/  reports/  settings/  migration/
│  │  │  └─ */[id]/page.tsx               # detail = legacy "Form View" tabs
│  │  ├─ api/                             # BFF route handlers: proxy + token refresh
│  │  └─ layout.tsx  globals.css
│  ├─ components/
│  │  ├─ ui/                              shadcn primitives (button, dialog, table, tabs…)
│  │  ├─ data-grid/                       DataGrid.tsx — legacy "Browse View": column reorder,
│  │  │                                    split-screen, saved views, live SignalR patching
│  │  ├─ command-palette/                 Ctrl+K (cmdk)
│  │  └─ forms/                           react-hook-form + zod field kit
│  ├─ lib/
│  │  ├─ api/                             generated TS client from OpenAPI
│  │  ├─ signalr/                         useHubConnection, useCartStream, useGridSubscription
│  │  ├─ auth/                            server-side session helpers (no token ever in JS)
│  │  ├─ hotkeys/                         global F-key + chord registry (doc 08)
│  │  └─ format/                          money, qty, date — locale aware
│  ├─ stores/                             Zustand: cartUi, stationStore, hotkeyStore
│  └─ types/                              generated from Retail25.Contracts
├─ tailwind.config.ts                     Slate/Zinc scale, no gradients (doc 08)
├─ next.config.mjs
└─ e2e/                                   Playwright: cash sale, split tender, bulk RFID, void
```

### Frontend package choices

| Concern | Package |
|---|---|
| Server state | `@tanstack/react-query` |
| Client/UI state | `zustand` |
| Realtime | `@microsoft/signalr` |
| Grids | `@tanstack/react-table` + `@tanstack/react-virtual` (10k-row browse views) |
| Forms | `react-hook-form` + `zod` |
| Primitives | `@radix-ui/*` via shadcn/ui |
| Palette | `cmdk` |
| Charts (reports) | `recharts` |
| Tables → Excel | server-side `ClosedXML` export (no client lib) |

---

## Conventions

- **One command/query per file**, named `VerbNounCommand` / `GetNounQuery`, with its handler,
  validator and response nested in the same file. No `Services` god-classes.
- **No EF Core types above Infrastructure.** Application talks to `IApplicationDbContext` exposing
  `DbSet<T>` and `SaveChangesAsync`; queries project to DTOs with `Select` (never return entities).
- **Domain has no I/O.** Pricing, tax, costing, order-quantity formulas and the EPC state machine
  are pure functions — that is what makes the golden-file parity suite possible.
- **Every mutating endpoint takes an `Idempotency-Key`.** A cashier double-tapping "Pay" over a
  flaky Wi-Fi link must not create two sales.
- **Migrations are explicit and reviewed.** `dotnet ef migrations add` output is committed; no
  `EnsureCreated`, no auto-migrate in production (a startup task applies them only when
  `Database:AutoMigrate=true`, which is dev/staging only).
