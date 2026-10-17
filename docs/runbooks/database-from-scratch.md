# Building a Retail 25 database from scratch

Standing up a working database on a machine that has never run this system, using nothing but this
repository. Written for someone who did not build it.

**No shop's data is copied by this procedure.** What you get is the real schema, the configuration
a till cannot run without, and a catalogue of invented merchandise.

If you want a database that already has things in it, there is a sanitised sample at
[`docs/sample-data/`](../sample-data/README.md) — a real database with every person taken out of
it. §7 explains the difference between that and a raw backup, and why only one of the two belongs
in a repository.

---

## 0. What this gives you, and what it does not

**You get a shop that works, stocked with things that do not exist.** Departments, categories,
products across three price levels, and RFID tags sitting on some of them. Enough to ring up a sale,
run a stock count, print a receipt and demonstrate bulk tag reading. It is not anybody's real
inventory and the numbers in the reports mean nothing.

**You need the .NET SDK.** The schema is applied by EF Core migrations, which run through
`dotnet`. There is no SQL script you can run instead, and §6 explains why the one in
`docs/architecture/` is not it. As of writing, the SDK is *not* installed on the machine this was
written from — if `dotnet --version` says the command is not recognised, install the SDK matching
`net10.0` before going further.

**Never point this at a database that has real data in it.** `Database:Seed` is safe to re-run —
the seeders check before they write — but `Demo:SeedCatalogue` puts a hundred-odd fictional products
into the catalogue, and they will turn up in a stock count. The demo seeder marks everything it owns
with a `DEMO-` stock-code prefix so you can find and delete it again, which is the only reason that
is recoverable.

---

## 1. Create an empty database

SQL Server, any edition — Express is what development uses.

```sql
CREATE DATABASE retail25;
```

That is the whole step. Do not create any tables; the migrations do that, and a database that
already has them will stop the migration dead.

---

## 2. Configure the API

Everything below is a secret or a per-machine path, so it goes in **user-secrets**, not in a file
you commit. From `backend/src/Retail25.Api`:

```bash
dotnet user-secrets init
```

Then set each of these. The connection string first:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.\SQLEXPRESS;Database=retail25;Integrated Security=true;TrustServerCertificate=true"
```

The three switches that turn bootstrap on. All three are off by default, which is why production
never does any of this by accident:

| Key | Set to | What it does |
|---|---|---|
| `Database:AutoMigrate` | `true` | Applies every migration on start. Without it the API starts against an empty database and fails on the first query. |
| `Database:Seed` | `true` | Runs the three seeders in §3. Requires `AutoMigrate`. |
| `Demo:SeedCatalogue` | `true` | Adds the invented merchandise. Leave this **off** for a real shop. |

And the administrator, without which nobody can sign in. The code says so in as many words: if no
users exist and these two are unset, start-up stops with *"nobody can sign in"* rather than leaving
you with a running API and no way in.

```bash
dotnet user-secrets set "Auth:AdminEmail"    "admin@example.invalid"
dotnet user-secrets set "Auth:AdminPassword" "<choose one, 8 characters or more>"
```

Optional, and worth setting:

- `Auth:AdminPin` — the till PIN for that administrator.
- `Auth:CashierEmail` / `Auth:CashierPassword` / `Auth:CashierPin` — a second, non-administrator
  user, which is the account you should actually demonstrate with.
- `Auth:WebOrigin` — defaults to `http://localhost:3000`. Set it to wherever the front end is
  served from, or sign-in redirects land nowhere.
- `Auth:AgentClientSecret` — the terminal agent's credential. Needed before a till can read a tag,
  not before the API starts.

---

## 3. Run it once

```bash
cd backend/src/Retail25.Api && dotnet run
```

Start-up does four things in a fixed order, and the order matters:

1. **Migrate.** `MigrateAsync`, never `EnsureCreated` — the latter builds the schema but writes no
   migration history, so the database could never be upgraded afterwards.
2. **`DatabaseSeeder`** — the configuration a shop cannot run without: tax, policy, the pricing
   ladder, document numbering, loyalty, tender types, and the first station.
3. **`IdentitySeeder`** — roles, permissions, the administrator, the agent's client. It runs after
   the store seed because the administrator's staff profile needs a location to belong to.
4. **`DemoDataSeeder`** — the invented catalogue, and only if `Demo:SeedCatalogue` is set.

Watch the log. Each seeder says what it wrote.

---

## 4. What you should have

Counted on the reference machine after a seed. Treat these as the right order of magnitude rather
than exact targets — that database has also had rows added by hand during hardware testing, and
those are included below where they cannot be told apart.

| | Rows | From |
|---|---|---|
| Permissions | 61 | `IdentitySeeder` |
| Roles | 5 | `IdentitySeeder` |
| Tender types | 7 | `DatabaseSeeder` |
| Locations / currencies / stations | 1 each | `DatabaseSeeder` |
| Departments | 8 | `DemoDataSeeder` |
| Categories | 17 | `DemoDataSeeder` |
| Products with a `DEMO-` prefix | 136 | `DemoDataSeeder` |
| Serialised units (RFID tags) | ~1,350 | `DemoDataSeeder`, 24 per tagged product |

Only apparel, electronics and hardware get tags. That is deliberate: it is how a real shop rolls
RFID out, one department at a time, and it means the system has to cope with a mixed catalogue
rather than assuming everything is tagged.

The demo catalogue is generated from a fixed seed, not from `Guid.NewGuid` or the clock, so two
people who seed the same way get the same products and a bug can be reproduced from a stock code
alone.

---

## 5. Check it actually works

**The arithmetic.** A 30.00 item under the seeded 5% and 7% taxes must quote **33.60**. If it does,
the whole chain — configuration, pricing engine, cart — is intact. If it does not, stop here; the
problem is in the seed, not in whatever you were about to do next.

**The sign-in.** Use the cashier account rather than the administrator. An administrator can do
everything, so signing in as one proves nothing about whether permissions were seeded correctly.

**The health endpoints.** `/health/live` says the process is up; `/health/ready` says it can reach
the database. A ready check that fails while live succeeds is almost always the connection string.

---

## 6. About `docs/architecture/schema.sql`

It is a **reference, not an installer.** Generated with SMO from a database carrying every applied
migration: 116 tables across `dbo` and `HangFire`, with their keys, constraints, indexes and
defaults, and not one row of data.

Do not build a database by running it. You would get the right tables and an empty
`__EFMigrationsHistory`, so EF would then try to apply every migration from the beginning against
tables that already exist, fail, and leave you with a database that can never be upgraded. Use §3.

Read it when you want to know what a column is actually called or how an index is defined without
starting SQL Server.

**To regenerate it** after adding a migration, script a database that has the new migration applied.
The canonical generator is EF:

```bash
dotnet ef migrations script --idempotent --project backend/src/Retail25.Infrastructure --startup-project backend/src/Retail25.Api --output docs/architecture/schema.sql
```

If the SDK is unavailable, SMO against a migrated database produces equivalent DDL — that is how the
current file was made, with `ScriptData` off so no row could be included.

---

## 7. Why there is no raw backup in this repository

A backup of a working shop carries customer names and addresses, accounts-receivable balances,
staff accounts and their password hashes — and git keeps it for ever. Deleting the file in a later
commit removes it from the working tree and not from the history, so the exposure outlives the
mistake by exactly as long as the repository does. A private repository changes who is exposed, not
whether: everyone with access, now and in future, gets the lot.

The sanitised sample in [`docs/sample-data/`](../sample-data/README.md) is a different thing, and
the difference is not one of degree. It is a copy that had every person removed from it before it
was ever written to a file — names, addresses, phone numbers, e-mail, PINs, tokens, keys, the audit
log — by a script committed beside it, so you can check rather than trust. What survives is
merchandise, stock, tags, prices and sales: the part that makes the system worth looking at and
identifies nobody.

So: hand out the sanitised sample freely. Never commit a `.bak`.

If someone needs *this shop's* real data to reproduce a bug, send them the one table they need,
outside version control, and delete it when the bug is closed.

The reference machine shows why you cannot shortcut this by eye: of 170 products in its database,
136 are seeded demo rows and the other 34 were added during hardware testing, alongside four real
user accounts and a real customer. There is no clean line through it that a person would reliably
draw twice — which is the whole argument for a script that draws it the same way every time.
