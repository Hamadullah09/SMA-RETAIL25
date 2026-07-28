# Running Retail25 in Visual Studio 2022

A step-by-step guide to getting the backend running on a Windows machine. Assumes nothing beyond a
fresh Visual Studio install.

Roughly 20 minutes the first time, most of it waiting for downloads.

---

## 1. What you need

| Requirement | Why | Where |
|---|---|---|
| **Visual Studio 2022**, 17.8 or newer | .NET 8 support | [visualstudio.microsoft.com](https://visualstudio.microsoft.com/downloads/) |
| ↳ workload **ASP.NET and web development** | Builds and debugs the API | Visual Studio Installer |
| **.NET 8 SDK** | The solution targets `net8.0` | Usually installed with the workload; otherwise [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **PostgreSQL 16** | The database | [postgresql.org/download/windows](https://www.postgresql.org/download/windows/) |
| **Node.js 20 LTS** | Only for the web front end | [nodejs.org](https://nodejs.org/) |
| **Redis** *(optional)* | Shared carts across tills | Skip it to start — see [§7](#7-optional-redis) |

> **Checking the workload is present:** open the **Visual Studio Installer**, click **Modify** on
> Visual Studio 2022, and confirm **ASP.NET and web development** is ticked.

Verify from a terminal:

```bash
dotnet --version
```

Anything starting `8.` or higher is fine. The repository targets .NET 8 but builds on the .NET 9 and
10 SDKs as well.

---

## 2. Install PostgreSQL

Run the installer and accept the defaults, with two things to note:

- **Remember the password** you set for the `postgres` superuser.
- Keep the port as **5432**.

You do not need Stack Builder at the end; click **Cancel** when it offers.

### Create the database

Open **pgAdmin** (installed alongside PostgreSQL), or use the SQL Shell (`psql`). Run:

```sql
CREATE USER retail25 WITH PASSWORD 'retail25-dev-password';
CREATE DATABASE retail25 OWNER retail25;
GRANT ALL PRIVILEGES ON DATABASE retail25 TO retail25;
```

> Use a different password if you like — just keep it consistent with §4.

---

## 3. Open the solution

1. Launch Visual Studio 2022 → **Open a project or solution**.
2. Navigate to the repository and open:

```
backend\Retail25.sln
```

3. Wait for **Restore** to finish in the status bar. First time, this downloads roughly 200 MB of
   NuGet packages.
4. Build with **Ctrl+Shift+B**.

You should see `Build: 11 succeeded, 0 failed`. If instead you see errors about a missing
`Microsoft.NETCore.App` reference pack, you are missing the .NET 8 targeting pack — install the
.NET 8 SDK from the link in §1 and reopen.

---

## 4. Point the API at your database

Create the file below. It is git-ignored, so your password stays on your machine.

**`backend\src\Retail25.Api\appsettings.Development.json`**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=retail25;Username=retail25;Password=retail25-dev-password"
  },
  "Database": {
    "AutoMigrate": true
  },
  "Seed": {
    "Enabled": true,
    "LocationCode": "MAI",
    "LocationName": "Main Store",
    "CurrencyCode": "USD",
    "CurrencyName": "US Dollar",
    "CurrencySymbol": "$",
    "MinimumTender": 0.01,
    "Tax1Name": "GST",
    "Tax1Rate": 5.0,
    "Tax2Name": "PST",
    "Tax2Rate": 7.0,
    "Tax2Compound": false,
    "StationCode": "001",
    "StationName": "Front Counter",
    "AdminEmail": "admin@retail25.local",
    "AdminPassword": "ChangeMe!2026",
    "AdminDisplayName": "Administrator"
  },
  "AllowedOrigins": [ "http://localhost:3000" ]
}
```

### What these settings do

| Setting | Effect |
|---|---|
| `Database:AutoMigrate` | Applies migrations at start-up. Convenient locally; **leave it off in production**, where migrations are a deliberate deployment step. |
| `Seed:Tax1Rate` / `Tax2Rate` | Written into the database as an effective-dated tax configuration. Changing them later is a settings change, not a redeploy. |
| `Seed:AdminPassword` | Minimum 12 characters. Without an administrator, nobody can sign in. |
| `Seed:*` generally | **Starting values only.** Everything becomes an editable row the moment it is written. |

> **Why the API refuses to price a sale without this.** There is no hardcoded tax rate anywhere in
> the system. If no tax configuration is in force for a location, the pricing engine returns
> `tax.not_configured` rather than guessing zero. That is deliberate — a till that silently charges
> no tax is worse than one that stops.

---

## 5. Run it

1. In the toolbar, set the startup project to **Retail25.Api** (the dropdown next to the green
   ▶ button; it may say *Retail25.Migration* by default).
2. Press **F5**.

A browser opens at the Swagger UI:

```
https://localhost:7xxx/swagger
```

The exact port is in `backend\src\Retail25.Api\Properties\launchSettings.json`.

### Confirm it worked

Watch the **Output** window (View → Output, "Show output from: Debug"). On a first run you should see:

```
Applying database migrations.
Seeded base currency USD.
Seeded location MAI — Main Store.
Seeded 4 price level definitions.
Seeded tax configuration effective 2000-01-01: GST 5%, PST 7%.
Seeded 7 tender types.
Seeded station 001.
Seeded role Administrator (legacy level 4).
Seeded administrator admin@retail25.local.
```

Then check health:

```
https://localhost:7xxx/health/ready
```

`Healthy` means the API can reach PostgreSQL.

---

## 6. Sign in and ring up a sale

Everything below can be done from the Swagger page.

### Sign in

`POST /api/v1/auth/login`

```json
{
  "email": "admin@retail25.local",
  "password": "ChangeMe!2026"
}
```

The session is an **httpOnly cookie** — Swagger holds it automatically, and no token is ever exposed
to JavaScript. Confirm with `GET /api/v1/auth/me`, which returns your permissions.

### Create a product

The seed sets up a store but no stock. Use `POST /api/v1/products` if present, or insert one
directly while the catalog endpoints are still being built:

```sql
INSERT INTO products (id, location_id, stock_code, name, type, regular_price,
                      tax1_applies, tax2_applies, on_hand, created_at)
SELECT gen_random_uuid(), id, 'SKU0001', 'Test Item', 0, 30.00, true, true, 100, now()
FROM locations WHERE legacy_code = 'MAI';
```

### Ring it up

1. `POST /api/v1/carts` — pass the `stationId` and `staffId` from the seeded rows.
2. `POST /api/v1/carts/{cartId}/lines` with `{ "identifier": "SKU0001" }`.
3. `GET /api/v1/carts/{cartId}/quote` — the priced cart.

With the seeded 5% and 7% taxes, a 30.00 item returns:

```json
{
  "subtotal": 30.00,
  "tax1Total": 1.50,
  "tax2Total": 2.10,
  "grandTotal": 33.60
}
```

If those numbers come back, the whole pricing chain is working: configuration → engine → cart.

---

## 7. Optional: Redis

Without Redis, carts live in the API process. Everything works, but a suspended sale cannot be
recalled at a different till, and a second API instance would not see it.

To enable it, add to `appsettings.Development.json`:

```json
"ConnectionStrings": {
  "Redis": "localhost:6379"
}
```

On Windows, the simplest options are Redis via WSL2 (`sudo apt install redis-server`), or
[Memurai](https://www.memurai.com/), a native Windows build.

The application picks the store automatically — Redis if a connection string is present, memory
otherwise. No code change either way.

---

## 8. Running the tests

**Test → Run All Tests** (or **Ctrl+R, A**). Expect **57 passing**.

| Project | What it covers |
|---|---|
| `Retail25.Domain.UnitTests` | 54 tests over pricing and tax, including 6 property tests × 500 randomly generated carts |
| `Retail25.ArchitectureTests` | The functional-parity benchmark |

The benchmark regenerates [`docs/BENCHMARK.md`](BENCHMARK.md) every run. It verifies each legacy
behaviour by reflection against the compiled assemblies, so a feature cannot be marked delivered
without the code that implements it.

---

## 9. Working with migrations

The **Package Manager Console** (Tools → NuGet Package Manager → Package Manager Console) with
*Default project* set to **Retail25.Infrastructure**:

```powershell
Add-Migration DescribeYourChange -OutputDir Persistence\Migrations
Update-Database
```

Or from a terminal:

```bash
dotnet ef migrations add DescribeYourChange --project backend/src/Retail25.Infrastructure --startup-project backend/src/Retail25.Infrastructure --output-dir Persistence/Migrations
```

If `dotnet ef` is not recognised:

```bash
dotnet tool install --global dotnet-ef --version 8.*
```

---

## 10. The front end

Node is not required for the API. To run the web app:

```bash
cd frontend
npm install
npm run dev
```

It serves on `http://localhost:3000`, which is already in `AllowedOrigins` above.

> The front end is mid-build and does not yet cover every screen. The API and Swagger are the
> reliable way to exercise the system today.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `28P01: password authentication failed` | Connection string password does not match the `retail25` role | Re-run the `CREATE USER` statement in §2, or correct the password in §4 |
| `3D000: database "retail25" does not exist` | `CREATE DATABASE` was skipped | Run it (§2) |
| `Skipping seed: N migration(s) have not been applied` | `AutoMigrate` is off and the schema is not current | Set `Database:AutoMigrate` to `true`, or run `Update-Database` |
| `No administrator seeded` in the log | `Seed:AdminEmail` / `AdminPassword` are missing | Add them (§4). Nobody can sign in until one exists |
| `Could not seed the administrator: Passwords must be at least 12 characters` | Password too short | Use 12+ characters |
| `tax.not_configured` from a quote | The location has no tax configuration in force | Check the seed ran; look in the `tax_configurations` table |
| `401 Unauthorized` on every call | Not signed in | `POST /api/v1/auth/login` first |
| Build error about `Microsoft.NETCore.App` 8.0 | .NET 8 targeting pack missing | Install the .NET 8 SDK |
| Startup project is wrong | Solution defaults to another project | Right-click **Retail25.Api** → **Set as Startup Project** |

---

## Where things live

```
backend/
  src/
    Retail25.Domain/          Entities and the pricing engine. No dependencies.
    Retail25.Application/     Commands, queries, orchestration.
    Retail25.Infrastructure/  EF Core, identity, Redis, seeding.
    Retail25.Api/             Controllers, SignalR hubs, host.
    Retail25.TerminalAgent/   Per-till service for RFID and peripherals.
    Retail25.Migration/       Legacy data importer.
  tests/
docs/
  BENCHMARK.md                Generated. What works, verified by reflection.
  architecture/               Design documents 01–12.
```
