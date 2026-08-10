# Deploying to myASP.NET shared hosting (pos.sma-techno.net)

Written against the live account `SMATECHNOLOGIES-001` on server `win8238.site4now.net`, August
2026. Where this document states a fact about the host — pool bitness, database engine, which
handler runs Node — it was read out of the control panel, not assumed.

## What runs where

One subdomain, two applications:

| URL | Application | IIS shape |
|---|---|---|
| `https://pos.sma-techno.net` | Next.js BFF (`server.js`) | site root, HttpPlatformHandler |
| `https://pos.sma-techno.net/backend` | ASP.NET Core API | IIS application under the root |

The API is at `/backend`, **not** `/api`. The front end already owns `/api/*` — `/api/auth/login`,
`/api/auth/session`, `/api/proxy/[...path]` are its own route handlers, and they are how sessions
are established. An IIS application mounted at `/api` shadows all of them and the site can no
longer sign anybody in. Nothing else about the layout is load-bearing; that is.

Consequences worth holding onto:

- The browser and the API are same-origin, so CORS never enters into it. `AllowedOrigins` still has
  to be right for the hub handshake.
- SignalR hubs are at `/backend/hubs/pos`, `/backend/hubs/inventory`, `/backend/hubs/rfid`.
- `NEXT_PUBLIC_API_URL` is **baked in at build time**, not read at runtime. Changing the public
  origin means rebuilding the front end, not editing an environment variable.

## Host facts that shaped the configuration

- **The application pool is shared.** `smatechnologies-001` serves `store1`, `assets-task`,
  `assets`, `saboor` and `POS`. Anything done to it is done to all five.
- **The API runs out-of-process.** The pool was 32-bit, and an in-process app inherits w3wp's
  bitness; the panel also refuses more than one in-process Core app per account. Out-of-process
  sidesteps both. See the comments in `backend/src/Retail25.Api/web.config`.
- **Node runs under HttpPlatformHandler**, which assigns a real TCP port through
  `%HTTP_PLATFORM_PORT%`. This is why the front end is deployed as a Next.js *standalone* build:
  iisnode would hand `server.js` a named pipe, `parseInt` would yield `NaN`, and Next would quietly
  listen on 3000 while IIS waited on a pipe.
- **SQL Server 2019** on `sql5063.site4now.net`, reached over the public internet. Remote access is
  how migrations get applied — there is no shell on the host.
- **No Redis, and none needed.** The host offers none, and Production refuses to start on in-memory
  stores. `Cache:Provider` is therefore `SqlServer`: the four things Redis held are tables in the
  application's own database. See "Cache stores" below for what that costs.

## Build

`.github/workflows/deploy-myasp.yml`, run from the Actions tab or by pushing a `v*` tag. It
produces three artefacts:

| Artefact | Contents |
|---|---|
| `retail25-backend` | API published framework-dependent, plus `web.config` |
| `retail25-frontend` | `server.js`, `.next/static`, `public`, `node_modules`, `web.config` |
| `retail25-database` | `migrate.sql`, idempotent |

The frontend job runs on **windows-latest** deliberately. npm resolves native optional dependencies
for the platform it installs on and Next's standalone trace copies whatever it finds, so a Linux
build ships Linux `.node` binaries into a tree that IIS runs under Windows. myASP.NET's own Next.js
article documents the resulting error (`Failed to load SWC binary for win32/x64`).

## One-time host setup

### 1. Application pool

Websites → pos → `…` → Application Pool. The pool must be **64-bit** (Actions → Change to 64-bit).
This restarts the pool, which briefly interrupts every site on it, and breaks any site depending on
a 32-bit-only native component — Jet/ACE OLEDB drivers and legacy COM being the usual ones. It is
reversible from the same menu.

### 2. Database

Databases → MSSQL → Add Database. Note the server, database name and login; the panel issues the
connection string. Append `Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True`.

Past roughly a hundred concurrent tills add `Max Pool Size=400;Min Pool Size=10` — ADO.NET caps a
pool at 100 and then queues, so outgrowing it looks like every till going slow at once rather than
an error.

### 3. Cache stores — nothing to do

`Cache:Provider` is `SqlServer`. Carts, RFID tag claims, idempotency records and hub tickets live in
the same database as everything else, created by the same migration. There is no Redis to
provision, no second service to keep up, and no second thing that can be down mid-sale.

What that costs, stated plainly:

- A database round trip on paths that used to hit memory. Tag claims are the hot one — a reader
  reports the same tag many times a second — which is why the claim is a single indexed statement
  rather than a read followed by a write.
- **One API instance only.** The stores themselves are safe across instances (the tag claim is a
  primary key settled by `MERGE … HOLDLOCK`; a ticket is redeemed by `DELETE … OUTPUT`, so neither
  can be won twice), but SignalR has no backplane without Redis. A hub message published by one
  instance would not reach a till connected to another, so a second cashier's screen would silently
  stop updating. Growing past one instance means moving `Cache:Provider` to `Redis`.
- Expired rows are swept from the write path, roughly every ten minutes, because a shared pool is
  recycled and unloaded when idle and a background timer would not survive it. Correctness does not
  depend on the sweep — every read filters on `expires_at`, so an unswept row is invisible rather
  than wrong.

### 4. Keys

OpenIddict signs and encrypts every token with these. They must be real files — the development
fallback keeps its key in the launching user's certificate store, which a shared pool never loads,
so keys would regenerate on each recycle and sign the whole shop out at unexplainable intervals.
The application refuses to start rather than let that happen.

**Use `.pem`, not `.pfx`, on this host.** PKCS#12 import fails here, and the error says nothing
useful:

```
CryptographicException: The system cannot find the file specified.
   at X509CertificateLoader.ImportPfx(...)
```

It names no file, and the `.pfx` it is complaining about is present and readable. Importing a .pfx
goes through the Windows certificate stack, which wants somewhere to materialise the private key —
a key container under a loaded user profile, and a writable temp directory. Neither
`X509KeyStorageFlags.EphemeralKeySet` nor switching the pool's **Load User Profile** to `True`
fixed it; both were tried. A PEM is parsed straight into an in-memory RSA key with no store, no
container and no temp file, so it works. Nothing here needs a certificate: OpenIddict publishes the
public half in its JWKS document and no client validates a chain.

Generate an encrypted PKCS#8 pair and upload both to `www\POS\backend\certs\`. They are not
reachable over HTTP — ASP.NET Core serves static files only from `wwwroot`.

Confirm afterwards that `/.well-known/jwks` returns one RSA key. That is the check that the signing
key actually loaded, as opposed to the process merely starting.

### 5. Node.js

Websites → pos → `…` → Node.js App → Enable, startup file `server.js`. Enabling it writes a
`web.config`; the one shipped in the frontend artefact replaces it and carries the environment
variables, so upload after enabling.

### 6. The API application

Websites → pos → `…` → Create .Net App, pointing at the `backend` folder under the site root. This
makes `/backend` a real IIS application with its own configuration rather than a folder — which is
what stops the parent site's Node handler from answering API requests.

## Configuration

Secrets go in pool **Environment Variables** (Application Pool → Actions → Environment Variables),
never in a committed file. Note the pool is shared with four other sites, so anything set there is
visible to all of them.

| Variable | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | from the panel, plus the flags above |
| `OpenIddict__SigningCertificatePath` | `certs/retail25-signing.pem` |
| `OpenIddict__EncryptionCertificatePath` | `certs/retail25-encryption.pem` |
| `OpenIddict__CertificatePassword` | generated with the certificates |
| `OpenIddict__Issuer` | `https://pos.sma-techno.net/backend` |
| `Auth__WebOrigin` | `https://pos.sma-techno.net` |
| `Auth__AdminEmail` / `Auth__AdminPassword` | the first administrator |
| `SESSION_SECRET` | 32+ random characters, front end only |

`AllowedOrigins` is an array and awkward as an environment variable; set
`AllowedOrigins__0=https://pos.sma-techno.net` in `appsettings.Production.json` on the host instead.

## Deploy

1. Download the three artefacts from the workflow run.
2. Zip each tree and upload through File Manager (Websites → pos → folder icon). Zip transfer,
   never file-by-file: the frontend tree is thousands of files and the account has a file quota.
3. **Put `app_offline.htm` in `www\POS\backend\` before replacing anything there, and delete it
   afterwards.** Windows locks the DLLs of a running process, and the File Manager's unzip
   overwrites what it can and says nothing about what it could not — so an upload reports success
   while the assembly it was meant to replace is untouched. The way this is noticed is a fix that
   demonstrably works locally having no effect on the server; the way it is diagnosed is comparing
   file timestamps in the File Manager, which is how it was found here (`web.config` updated,
   `Retail25.Api.dll` two hours stale).

   Restarting the pool is **not** a substitute: the process re-acquires the locks as soon as it
   comes back, and the upload has already silently failed by then. `app_offline.htm` is what the
   ASP.NET Core module watches for — it shuts the application down and releases the files, and
   only the API goes offline, so the shop front end keeps serving.
4. Unzip `retail25-frontend` into `www\POS\`, `retail25-backend` into `www\POS\backend\`.
5. Apply `migrate.sql` against the database — SSMS or Azure Data Studio pointed at
   `sql5063.site4now.net`. It is idempotent, so re-running it is safe. `Database:AutoMigrate` stays
   false: migrating from inside the web process races the first request after a deploy, and several
   worker processes may start at once.
6. Restart the pool.

## Verification

In this order, because each step's failure mode is distinct:

1. `https://pos.sma-techno.net/backend/health/live` — the process started.
2. `https://pos.sma-techno.net/backend/health/ready` — SQL answered. There is no Redis check under
   `Cache:Provider=SqlServer`, deliberately: a permanently red probe for a service the deployment
   does not use teaches people to ignore the endpoint.
3. `https://pos.sma-techno.net` — the front end renders with styling. Unstyled means `.next/static`
   was not copied alongside `server.js`.
4. Sign in. A login that appears to fail while succeeding is almost always `Auth__WebOrigin` or
   `OpenIddict__Issuer` disagreeing with the browser's address bar.
5. Open a till and add a line. The cart arriving without a WebSocket means the `<webSocket>` element
   was rejected and SignalR fell back to long polling — it works, at a round trip per update.

## When it does not start

`stdoutLogEnabled` is `true` in the shipped `web.config` for exactly this reason; the logs are in
`www\POS\backend\logs\`. **Turn it off once the site is up** — it grows without bound.

| Symptom | Cause |
|---|---|
| HTTP 500.30 | The app threw during startup. The stdout log has the exception; the two that produce it here are a missing certificate and a bad connection string. |
| HTTP 500.19 | `web.config` rejected. If it names the `webSocket` element, the shared plan has that section locked — remove the element, SignalR falls back to long polling. |
| HTTP 502.5 | The process could not launch. Usually the .NET 10 runtime is not installed on this server; ask the helpdesk. |
| Site loads unstyled | `.next/static` missing beside `server.js`. |
| Redirect loop | HTTPS not detected. Out-of-process hosting depends on the module's `X-Forwarded-Proto`; if it is absent, `UseHttpsRedirection` redirects forever. |
| Signed out at intervals | Ephemeral OpenIddict keys — the certificate paths are not resolving. |

## Known gaps

- **Hangfire's scheduled work does not run on its own.** `Jobs:RunServer` is false because a shared
  pool is recycled on a schedule and unloaded when idle, so a nightly 2am accrual would run only if
  somebody happened to be using the site. Drive it from Websites → pos → `…` → Schedule Tasks
  against the trigger endpoint.
- **The RFID terminal agent is not deployed here.** It talks to reader hardware over the local
  network and belongs on a machine in the shop, not on shared hosting. `Rfid:ServerReaders:Enabled`
  stays false.
- **`ci.yml` still asks for .NET 8** against a `net10.0` solution, and `deploy/Dockerfile.web`
  references a `frontend/retail25-web/` path that does not exist. Neither affects this deployment;
  both are wrong.
