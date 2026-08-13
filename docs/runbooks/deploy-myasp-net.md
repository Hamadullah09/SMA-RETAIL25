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
- **SQL Server 2022** on `sql5113.site4now.net`, database `db_acd077_pos`, login
  `db_acd077_pos_admin`, on a 1000 MB quota. Reached over the public internet on 1433 — remote
  access is how migrations get applied, because there is no shell on the host. An earlier revision
  of this document named `sql5063.site4now.net`, which is a different SQL host on the same estate;
  corrected 2026-08-13 against the control panel.
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
   `sql5113.site4now.net`. It is idempotent, so re-running it is safe. `Database:AutoMigrate` stays
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
| Sign-in ends on `localhost:<port>`, then "That form had expired" on the retry | A front-end redirect built from the *request's* origin rather than `APP_ORIGIN`. HttpPlatformHandler proxies to Node on a private port, so `request.nextUrl.origin` is `localhost:<HTTP_PLATFORM_PORT>` and a redirect derived from it goes somewhere only the server can reach. Note the sign-in has already **succeeded** by then — the session cookie is written and only the final hop is wrong — so what the user reports is whatever they try next, which is going back and resubmitting a form the API then correctly rejects as stale. Check `/api/auth/session` before believing sign-in is broken. |
| Signed out at intervals | Ephemeral OpenIddict keys — the certificate paths are not resolving. |

## Known gaps

- **Hangfire's scheduled work does not run on its own.** `Jobs:RunServer` is false because a shared
  pool is recycled on a schedule and unloaded when idle, so a nightly 2am accrual would run only if
  somebody happened to be using the site. Drive it from Websites → pos → `…` → Schedule Tasks
  against the trigger endpoint.
- **The RFID terminal agent is not deployed here.** It talks to reader hardware over the local
  network and belongs on a machine in the shop, not on shared hosting. `Rfid:ServerReaders:Enabled`
  stays false.
- **`deploy/Dockerfile.web` references a `frontend/retail25-web/` path that does not exist.** It
  does not affect this deployment, but the container build is broken. (The companion claim that
  `ci.yml` targets .NET 8 was stale and has been removed — it asks for `10.0.x` on both jobs.)

---

# Continuous deployment

Everything above describes the manual route, which remains the fallback. The normal path is now
automatic: **push to `deploy/myasp-net-pos` and the site updates itself.**

`.github/workflows/deploy-myasp.yml` runs tests, builds both applications, pushes them over Web
Deploy, and then proves the site answers before it calls the deploy a success.

## Why Web Deploy and not FTP

Probed from outside the host, August 2026:

| Port | Service | State |
|---|---|---|
| 21 | FTP | filtered |
| 990 | FTPS | filtered |
| 22 | SFTP/SSH | filtered |
| **8172** | **Web Deploy management service** | **open** |

The endpoint answers `WWW-Authenticate: Basic realm="WebManagementService"`, so Web Deploy is not
merely reachable, it is enabled. It is also the better tool for the job: it syncs only the files
that changed, and `-enableRule:AppOffline` performs the app_offline dance automatically — which is
the whole reason a manual upload used to silently fail to replace a loaded DLL.

## One-time setup

Three repository secrets are required. Get them from the control panel: **Websites → pos →
Publish Profile** (or *Web Deploy*), which downloads a `.PublishSettings` file. Open it in any text
editor; it is XML, and the three values are attributes on the `publishProfile` element with
`publishMethod="MSDeploy"`.

Web Deploy is **off by default** on this plan. Turn it on first: **Websites → pos → VS Webdeploy →
TURN ON**. The server-wide management service answers on 8172 either way, so a port probe is not
evidence the feature is enabled for the site.

Read from the live profile, August 2026:

| Secret | Where it comes from | Value |
|---|---|---|
| `MYASP_SITE_NAME` | `msdeploySite` | `smatechnologies-001-site5` |
| `MYASP_DEPLOY_USER` | `userName` | `smatechnologies-001` |
| `MYASP_DEPLOY_PASSWORD` | `userPWD` | ships **blank** — use the control-panel password |

The site name is neither the account name nor the website's display name ("pos"); it is the
`-siteN` form. Guessing it wastes a run, so take it from `msdeploySite`.

`publishUrl` confirms the endpoint host is `win8238.site4now.net`, which is what `WEBDEPLOY_HOST`
in the workflow is pinned to. The profile's FTP entry (`ftp://win8238.site4now.net:21/pos`) is not
usable: port 21 is firewalled from outside the datacentre.

Add them under **Settings → Secrets and variables → Actions → New repository secret**. The deploy
job binds to a `production` environment, so they can equally be set as environment secrets if you
later want a required reviewer before anything reaches the till.

The workflow checks all three are present and fails with a named list before it touches the host,
so a missing secret costs a fast red run rather than a half-deployed site.

**Do not commit the `.PublishSettings` file.** It contains the password in clear text.

## What happens on a push

1. **Gate** — domain, application, architecture and terminal-agent suites, plus the frontend
   typecheck and lint. A red gate stops everything; nothing is uploaded.
2. **Build** — the API published framework-dependent, and the Next.js standalone tree assembled on
   a *Windows* runner (a Linux build ships Linux `.node` binaries that IIS cannot load).
3. **Deploy the API first**, into `/backend`, with `AppOffline` so the loaded DLLs are released.
   The API is the dependency: if this fails, the front end is never touched and the site carries on
   serving the previous working pair.
4. **Deploy the front end** into the site root.
5. **Smoke test** — polls `/backend/health/ready` and `/` for up to five minutes. The first request
   after a deploy pays for a cold start *and* the startup migration, which has been measured at
   ~17 seconds on this host, so a single immediate check would report a false failure.

Documentation-only pushes are ignored (`paths-ignore`), because an edit to an audit note should not
restart a till.

## The two rules that protect the API

`/backend` is an IIS application **inside** the site root. Web Deploy's job is to make the
destination match the source, and the front-end artefact contains no `backend` folder — so a plain
root sync would delete the entire API.

The `-skip` rules in the root sync step are the only thing preventing that. If you edit that step,
keep them:

```
-skip:objectName=dirPath,absolutePath=.*\backend$
-skip:objectName=dirPath,absolutePath=.*\backend\.*
-skip:objectName=filePath,absolutePath=.*\backend\.*
```

`logs` is skipped for the mirror-image reason: it exists only on the host, and a sync would remove
it.

## Three directories that exist only on the host

Web Deploy makes the destination match the source. Anything living on the host but not in the build
is therefore a deletion candidate, and three of those matter. All three are `-skip`ped, and the
first dry run proved each one was genuinely at risk:

| Path | What losing it costs | When you would notice |
|---|---|---|
| `backend/certs/` | The OpenIddict signing and encryption keys. `IdentityRegistration` **throws on startup** outside Development when either is missing, so this is HTTP 500.30 and a dead API — not merely broken tokens. | Immediately |
| `.well-known/acme-challenge/` | Let's Encrypt's HTTP-01 challenge. The certificate silently fails to renew. | **~90 days later**, when HTTPS goes invalid |
| `logs/` | Host-side stdout logs. | When you next need them |

The middle one is the reason to keep running dry runs after any change to the sync steps: it breaks
nothing on the day, and by the time it surfaces nobody would connect it to a deploy.

`backend/Retail25.Api.exe` is also proposed for deletion and that one is safe to allow. `web.config`
runs `dotnet Retail25.Api.dll`, not the apphost, and a Linux publish produces no Windows `.exe`.

**Prove it before trusting it.** `workflow_dispatch` has a `dry_run` input that adds `-whatif` to
both syncs: Web Deploy reports every add, update and delete it *would* make and writes nothing.
Run it once before the first real deploy and read the root-sync output for any line mentioning
`backend`. There should be none. This is the cheapest possible check on the one mistake in this
pipeline that would take the API down.

## Migrations

Not run by the pipeline, and deliberately. `Program.cs` calls `MigrateAsync()` at startup, so the
schema comes up to date on the first request after a deploy. That means **CI never holds a database
credential** — the smaller attack surface is worth more than the control.

`migrate.sql` is still published as a build artefact for when a migration needs inspecting, or
applying by hand against a database the app has not yet reached.

## A note on the integration suite

The first version of this pipeline excluded the integration tests from the gate, on the assumption
that four known failures would otherwise block every deploy. They passed on the first CI run, so
the exclusion was removed and the gate gates on the whole solution.

The lesson is worth keeping: those four failures were a property of one developer's machine, not of
the code. If the suite proves genuinely flaky in CI later, the answer is `workflow_dispatch` with
`skip_tests` for the one deploy that cannot wait — not a permanently narrowed gate, which quietly
stops being a gate at all.

## Rolling back

Deploys are builds of a commit, so a rollback is a deploy of the previous commit:

```bash
git revert <bad-commit> && git push
```

If the pipeline itself is the problem, **Actions → Deploy (myASP.NET) → Run workflow** takes a
branch or tag and has a `skip_tests` input for when you need the previous build back immediately
and already know it was green.

The manual route above still works and needs nothing from GitHub.
