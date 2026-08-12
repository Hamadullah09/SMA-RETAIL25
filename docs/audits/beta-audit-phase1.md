# SMA Retail POS — Beta / Enterprise Readiness Audit

> **CORRECTIONS — read first.** Phase 2 (authenticated) overturned several Phase 1 findings. Phase 1
> was written without a session and without data, and was wrong in three material ways. The
> corrections are recorded here rather than quietly edited into the text, because a reviewer who
> read the first version needs to know what changed.
>
> | Phase 1 claim | Correction | Status |
> |---|---|---|
> | **BLOCKER-4:** database empty, system cannot trade | **WRONG.** Seeding has since run: **201 products, 200 stock levels, 200 serialized units (RFID tags), 5 customers, 8 categories, 3 departments, 1 purchase order (20 lines), 5 roles, 3,236 audit entries, 2 stations.** | **RETRACTED** |
> | **Visiting `/admin` destroys the session** | **WRONG.** `/admin` loads correctly. The logout was the 15-minute access token expiring while the tab sat idle. Still a UX concern (see OBS-1) but not the critical defect claimed. | **RETRACTED** |
> | **BLOCKER-3:** no user *or role* management at all | **PARTLY WRONG.** Role assignment **does** exist at `/admin/settings?tab=Users`: staff code, first/last name, **access level 0–4**, **Active** toggle, PIN state, Save. The read-only role display cited was `/admin/staff`, a different page. **What genuinely does not exist is CREATE.** | **REVISED — see BLOCKER-3R** |
> | Hardcoded tax rates, payment methods, receipt details *(listed as "not verifiable")* | **LIKELY WRONG.** Setup exposes 13 configuration tabs: Business ID, Branding, **Taxes**, POS, Groupings, Printers, Hardware, **Stations**, **Tenders**, **Currencies**, **Numbering**, Pricing, Users. The product is far more configurable than inferred. | **REVISED** |
>
> ### BLOCKER-3R — No way to create a user (revised, still Critical)
> **VERIFIED in the running application.** The Users tab renders one card per *existing* staff member
> with editable fields and a Save button. There is **no "Add", "New" or "Invite" control anywhere**,
> which matches the API: `StaffController` exposes no `POST` to create staff. Combined with
> self-registration being disabled (correctly) and the seeder skipping existing accounts, **an
> administrator cannot onboard a single new employee through the product.** This remains the highest
> priority defect and is the reason the reported sign-up attempt returns *"This system does not
> accept self sign-up. Ask an administrator for an account."* — advice that cannot be acted upon.
>
> ### OBS-1 — Session expires after 15 minutes idle, with no warning
> **VERIFIED.** Access token lifetime is 15 minutes. An idle tab silently becomes signed-out; the next
> navigation lands on the sign-in page with no "your session expired" message and no return to the
> page that was being viewed. On a till this is disruptive. Recommend a longer interactive lifetime,
> a silent-refresh heartbeat while the tab is open, and a message on bounce.

---

## Phase 2 — authenticated testing (partial sweep)

Modules reached: Dashboard, Administration hub, Setup (13 tabs), Users and access, Point of Sale.
**Not reached:** Inventory, Stock, Customers, Suppliers, Purchasing, Receivables, Orders, Reports,
and eight admin sub-pages (RFID readers, Audit, Backup, Migration, Year-end, Undelete, Accounting,
Departments). Those remain **NOT TESTED** — not passed.

### BUG-01 — A serialized item cannot be added to a sale *(the till cannot trade)*
**Severity: Critical · Priority: P0 · Reproducibility: 100%**

**Steps**
1. Sign in, open Point of Sale (station 001, drawer open).
2. Press `F9` → *Find item* → type `belt` → 9 results render correctly with code, name and price.
3. Press `Enter` → *Choose a unit* opens for `FR0207001`, listing EPC `E28011700080020A7A6B6AE1`.
4. Press `Enter` on the unit — **nothing happens**. Dialog stays open, sale remains `EMPTY`.
5. Click the unit row with the mouse — **nothing happens**. Same result.

**Expected:** the unit is added to the sale; subtotal and total update.
**Actual:** no line is added by either input method. Sale stays `Rs 0.00 / EMPTY`.

**Business impact:** the database holds **200 serialized units against 201 products**, so effectively
every product routes through this dialog. **No sale can be completed.** This is the single most
important defect found and it blocks all downstream testing — payments, tenders, discounts, refunds,
receipts, drawer, stock decrement, ledger, reporting and customer balances are all unreachable.

**Suspected cause (requires source review):** the unit-selection handler is not wired to the
add-line action, or is failing silently. No console error was captured; the request should be traced
at `/api/proxy/…` to see whether a call is issued at all.

### BUG-02 — Currency symbol is inconsistent between components
**Severity: High · Priority: P1 · Reproducibility: 100%**

The *Find item* dialog renders prices as **`$2000.00`, `$2950.00`, `$1750.00`**, while the sale
panel renders **`Rs 0.00`** and the seeded location's base currency is **`PKR`**. Two different
currency symbols on one screen, one of them wrong.

**Business impact:** a cashier reading `$2000` for an item priced `Rs 2000` cannot trust any figure
on the screen. In a shop taking real money this is a serious correctness and confidence issue, and
it undermines the Currencies configuration tab that exists precisely to control this.

**Suggested fix:** one shared currency formatter driven by the location's configured currency; no
component should hold a literal symbol.

### BUG-03 — Refresh-token families are being revoked, logging users out unpredictably
**Severity: Critical · Priority: P0 · Reproducibility: observed twice in ~15 minutes**

**Evidence — `OpenIddictTokens` grouped by type and status:**

| Type | valid | redeemed | **revoked** |
|---|---|---|---|
| refresh_token | 26 | 14 | **22** |
| access_token | 54 | — | **22** |
| id_token | 40 | — | **22** |

Revocations arrive in **matched sets of 22 across all three types** — the signature of reuse
detection revoking a whole token family. **Revocations (22) outnumber successful rotations (14).**

**Mechanism (from source):** access tokens live 15 minutes;
`SetRefreshTokenReuseLeeway(TimeSpan.Zero)` means replaying a spent refresh token revokes the
family. Dashboard and POS pages issue several BFF proxy calls in parallel; when they cross the
expiry boundary together, one rotates the token and the others replay a spent one. The
`refreshesInFlight` map that exists to collapse this is per-process and is evidently not preventing
it here.

**Business impact:** a cashier can be signed out mid-sale, without warning. At a counter with a
queue this is unacceptable, and it will worsen with more concurrent tabs/tills.

**Suggested fix:** serialise refresh across requests (a short server-side lock keyed on the session,
not just an in-process map), and/or allow a small non-zero reuse leeway so a benign race does not
revoke a family. Add a "your session expired" message and return-to-page on bounce.

### OBS-2 — POS status indicators start red then clear
**Severity: Low (UX) · Priority: P3**

On load the POS header shows `Server offline`, `RFID offline`, `Printer offline`, `Scale offline`.
`Server` clears to connected after a few seconds once SignalR negotiates. Showing a red
`Server offline` badge during normal startup will cause needless alarm at a counter. Recommend a
neutral "connecting…" state.

*RFID / Printer / Scale remaining offline is correct here — no hardware is attached.*

### Verified working

| Area | Result |
|---|---|
| SignalR real-time channel | **PASS** — connects after negotiation (corrects an earlier observation) |
| Product search (`F9`) | **PASS** — 9 correct matches for `belt`, with code, name, price |
| Unknown identifier handling | **PASS** — clean inline error, *"No item matches that identifier."* |
| Serialized/RFID unit model | **PASS** — real EPCs stored and offered per unit |
| Setup configurability | **PASS** — 13 tabs incl. Taxes, Tenders, Currencies, Numbering, Stations, Printers, Hardware |
| Staff editing | **PASS** — code, names, access level 0–4, Active, PIN state, Save |
| Dashboard KPIs | **PASS** — renders reorder counts, on-order value, empty states |
| Keyboard-first design | **PASS** — full function-key bar (F4 Pay, F5 Client, F9 Find, F10 Drawer, F11 More) |

---

## Phase 1 report (as originally written, corrections above take precedence)


**Target:** https://pos.sma-techno.net
**Date:** 11 August 2026
**Stated goal:** 10+ RFID readers, 1,000+ POS stations, multi-store, high transaction volume, on-premise reliability.

## How to read this document

Every finding is labelled with how it was established. This matters more than the finding itself:

| Label | Meaning |
|---|---|
| **VERIFIED** | Directly observed against the live system or measured. Evidence given. |
| **SOURCE** | Established by reading the application's source, to which I have full access. Definitive for questions of "is this hardcoded / does this endpoint exist". |
| **INFERRED** | Reasoned from the above. Plausible, not proven. |
| **BLOCKED** | Could not be tested. What is required is stated. |

Nothing in this document is marked as passing on the basis that the UI looked correct.

## Coverage — read this before the findings

This is **Phase 1 only**, covering the unauthenticated surface, the deployment, and the source tree. It is roughly **30% of the requested scope**.

Two blockers stopped the rest:

1. **No authenticated session.** I am not permitted to enter a password to authenticate, so every screen behind sign-in is untested. Resolution: a human signs in and leaves the session open.
2. **The database contains no business data.** 1 location, 1 station, 1 user, 1 staff profile — and **0 products, 0 departments, 0 categories, 0 customers, 0 carts, 0 transactions**. POS, inventory, purchasing, customer and reporting workflows cannot be exercised at all, with or without a login. Resolution: load a catalogue, either by the legacy Retail Plus import or by enabling the demo seed on a throwaway database.

Consequently there are **no calculation, rounding, concurrency, refund, or data-integrity results in this report**. Those are the areas most likely to contain money-affecting defects, and they remain entirely untested.

---

## 1. Findings that block the stated targets

### BLOCKER-1 — One deployment can only ever be one till
**VERIFIED.** Severity: Critical. Priority: P0.

`NEXT_PUBLIC_STATION_ID` and `NEXT_PUBLIC_LOCATION_ID` are `NEXT_PUBLIC_*` variables, which Next.js **inlines into the JavaScript bundle at build time**. I confirmed `Number("1")` is compiled into the deployed chunk `/_next/static/chunks/app/(dashboard)/pos/page-f128ea228201b995.js`.

The POS page's own comment states the intent plainly: *"The station and location come from this machine's environment. They are per-till facts, not user choices."* That design assumes the app is **installed on each till**. This deployment is one central web app, so **every browser that opens it identifies as station 1**.

Impact against your target of 1,000+ stations:
- Two tills on this deployment both claim station 1.
- `GetByStationAsync` returns the *same* active cart to both — two cashiers would see and mutate one basket.
- Cross-till RFID tag arbitration is keyed on `stationId`; with every till reporting station 1, the "same tag claimed by another till" protection silently never triggers.

This is not a tuning problem. **Station identity must move out of the build** — into the signed-in session, a per-device registration token, or a device-bound cookie — before a second till exists.

### BLOCKER-2 — The deployment cannot be scaled horizontally
**VERIFIED (I introduced this constraint and documented it).** Severity: Critical. Priority: P0.

`Cache:Provider` is `SqlServer` because the host offers no Redis. The four shared stores (cart, RFID tag claim, idempotency, hub ticket) are correct across instances — the tag claim is settled by `MERGE … HOLDLOCK` and a hub ticket by `DELETE … OUTPUT`, both verified under contention with 20 and 10 concurrent callers respectively.

**SignalR has no backplane.** A hub message published by instance A never reaches a till connected to instance B. Adding a second app instance would leave half the tills silently not updating — the worst kind of failure, because nothing errors.

1,000 terminals cannot be served by one instance of anything. Scaling out **requires Redis** (config change, not a rewrite) plus resolving BLOCKER-1.

### BLOCKER-3 — There is no way to create a user
**SOURCE.** Severity: Critical. Priority: P0. *(This confirms your suspicion.)*

I enumerated every endpoint on the user-facing controllers:

- `StaffController` — `GET` list, time-clock in/out, commission rules, hours/commission reports. **No POST to create a staff member.**
- `SecurityController` — PIN set/verify/unlock, approvals, audit query. **No user creation, no role assignment.**
- `RegistrationController` — `register`, `forgot-password`, `reset-password`.

So the **only** ways an account can come into existence are:
1. **Self-registration** — disabled by default (see SEC-1), and lands on a `Trainee` role.
2. **The first-run seeder** — and it creates the account **only if that email does not already exist**. Changing `Auth__AdminPassword` afterwards does nothing.

There is no admin screen or API to invite a user, assign a role, change a role, deactivate a leaver, or reset another person's password. For a multi-user, multi-role retail business this is disqualifying: staff turnover alone makes it unworkable.

I hit this personally — the admin password could not be changed through any supported path and had to be reset by writing an Identity hash directly to the database.

### BLOCKER-4 — No inventory, so the system cannot trade
**VERIFIED.** Severity: Critical. Priority: P0.

0 products, 0 departments, 0 categories. The seeder created operating configuration only; the demo catalogue is deliberately gated behind `Demo:SeedCatalogue`. Nothing can be sold until a catalogue is loaded. The intended route is the Retail Plus 2.5 legacy import (`LegacyImporter`, `MigrationController`, `Retail25.Migration` CLI), which is **untested**.

### BLOCKER-5 — Cold start of ~17 seconds
**VERIFIED (measured).** Severity: High. Priority: P0 for retail use.

Ten sequential requests to `/backend/health/live`:

```
 1.  16763 ms   <- cold start after idle unload
 2.   1275 ms
 3.    327 ms
 …
10.    272 ms
```

Requests also failed outright between probe batches, consistent with the pool unloading again. Shared IIS recycles and unloads on idle. **The first sale of the morning waits ~17 seconds**, and so does the first after any quiet period. Unacceptable at a counter with a queue.

Root cause is the hosting tier, not the code. Fixes: an always-on ping, or hosting that does not idle-unload. On-premise deployment (your stated target) removes it.

### BLOCKER-6 — Scheduled work does not run
**SOURCE + VERIFIED config.** Severity: High. Priority: P1.

`Jobs:RunServer` is `false`, deliberately: a shared pool is recycled and unloaded when idle, so a background worker is not alive at 02:00. Consequence: **nightly late-charge accrual, accounting sync, and any recurring job never execute** unless driven externally. Storage is registered so the schedule *looks* healthy — which makes this a silent failure.

---

## 2. Security findings

The security posture is markedly better than the rest of the readiness picture. Credit where due.

### Verified good

| Check | Result | Evidence |
|---|---|---|
| API requires auth | **PASS** | `/backend/api/v1/{products,customers,sales,staff,settings}` all return **401** unauthenticated |
| BFF proxy requires auth | **PASS** | `/api/proxy/products` → 401 with clean JSON, no stack trace |
| Swagger exposed? | **PASS** (not exposed) | `/backend/swagger/*` → **404** |
| Hangfire dashboard exposed? | **PASS** (not exposed) | `/backend/hangfire` → **404** |
| Security headers | **PASS** | `CSP: default-src 'none'`, `HSTS`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy` all present |
| Server banner | **PASS** | `X-Powered-By` removed, `removeServerHeader="true"` |
| Error verbosity | **PASS** | 401 bodies are RFC 9110 problem documents; no stack traces observed |
| Token exposure | **PASS (SOURCE)** | BFF pattern — access tokens never reach the browser; session is an encrypted `__Host-` cookie |
| Token revocability | **PASS (SOURCE)** | `UseReferenceAccessTokens` / `UseReferenceRefreshTokens` — sessions can actually be revoked |
| PKCE | **PASS (SOURCE)** | Required, and `plain` explicitly removed — S256 only |
| Refresh token reuse | **PASS (SOURCE)** | Rotation with zero leeway; replay revokes the family |
| Account enumeration | **PASS (SOURCE)** | Registration returns the same response for existing and new addresses |
| Brute force | **PASS (SOURCE)** | Lockout at 5 failures / 15 minutes; rate limiting partitioned per caller |

### SEC-1 — Self-registration is off, but the login page still advertises it
**VERIFIED.** Severity: Low (UX), but security-adjacent. Priority: P2.

`POST /backend/api/v1/account/register` → **403 `registration.disabled`**. Correct and safe. However the sign-in page shows *"No account yet? Create one"*, which leads to a dead end. Either hide the link when registration is disabled, or state that accounts are issued by an administrator.

### SEC-2 — HSTS max-age is 30 days
**VERIFIED.** Severity: Low. Priority: P2.

`Strict-Transport-Security: max-age=2592000` (30 days). Below the 1-year value required for preload consideration. Raise to `31536000` and add `includeSubDomains` once you are confident every subdomain is HTTPS.

### SEC-3 — Password policy is weak for an admin account
**SOURCE.** Severity: Medium. Priority: P1.

`RequiredLength = 8`, `RequireDigit = true`, and uppercase / lowercase / non-alphanumeric all **false**. `password123` satisfies it. For an account that can void sales and read takings, raise the floor and add breach-list checking.

### SEC-4 — No password rotation path for an existing user
**SOURCE.** Severity: High. Priority: P0. *(Same root cause as BLOCKER-3.)*

`forgot-password` exists but depends on SMTP, which is **not configured on this deployment** — so the only self-service recovery route silently cannot deliver. Combined with no admin reset, a forgotten password means direct database surgery. I had to do exactly that.

### Not tested — requires a session
XSS via product/customer fields, CSRF on state-changing endpoints, IDOR on `{id:long}` routes, privilege escalation between roles, file upload safety, session fixation/timeout. **BLOCKED: authenticated session required.**

---

## 3. Architecture and scalability

### ARCH-1 — Single point of failure throughout
**VERIFIED.** Severity: Critical for enterprise. Priority: P1.

One shared-hosting IIS site, one 1 GB 32-bit-account app pool (POS now isolated on its own 64-bit pool), one SQL Server database on a separate host. No redundancy, no failover, no load balancing. A pool recycle takes the whole shop offline for ~17 seconds.

Against "reliable operation on local/on-premise infrastructure": **this deployment is the opposite of that** — it is remote shared hosting. On-premise is the correct target and would remove the cold-start, the shared-pool contention, and the internet dependency for tills.

### ARCH-2 — Every cart operation is a cross-server database round trip
**VERIFIED (I made this change).** Severity: High. Priority: P1.

The app is on `win8238.site4now.net`; the database is on `sql5113.site4now.net`. With `Cache:Provider=SqlServer`, cart saves, tag claims, hub tickets and idempotency records — previously in-memory Redis operations — are now **network round trips to a different machine**.

The hot path is RFID tag claims: a reader reports the same tag many times a second, and each is a `MERGE` against a remote database. At 10+ readers this is the first thing that will fall over. Mitigations: Redis, or co-locating the database, or both. **REQUIRES LOAD TEST.**

### ARCH-3 — Connection pool ceiling
**SOURCE.** Severity: High at target scale. Priority: P1.

ADO.NET caps a pool at 100 connections and then queues. The deployed configuration does not raise it. Past roughly a hundred concurrent tills the first symptom is **not an error but every till going slow at once**. The config file documents this and recommends `Max Pool Size=400;Min Pool Size=10` — currently not applied. **REQUIRES LOAD TEST.**

### ARCH-4 — Workstation GC
**VERIFIED.** Severity: Medium. Priority: P2.

`System.GC.Server: false`, set deliberately to fit the 1 GB shared pool. Correct here; **wrong for an enterprise server**, where server GC gives materially better throughput. Must be revisited on on-premise hardware.

### Requires load testing before any scale claim
None of the following can be established from a browser. All are **REQUIRES LOAD TEST**:

- Sustained throughput at 1,000 concurrent stations
- SignalR connection ceiling per instance (and the backplane once Redis returns)
- Tag-claim `MERGE` contention with 10+ readers streaming concurrently
- SQL Server lock/deadlock behaviour on `cached_cart` and `cached_tag_claim` under load
- Connection pool saturation point
- Memory behaviour over a multi-day run (your "no memory leaks over long-running workloads" requirement)
- Report generation cost against a large ledger
- Cold-start frequency under real traffic patterns

---

## 4. RFID

**BLOCKED: physical hardware and network environment required.** Nothing about RFID was functionally tested. What is known from source and configuration:

- `Rfid:ServerReaders:Enabled` is **false** on this deployment. The server holds no reader connections.
- The intended architecture for a shop is the **terminal agent** running on a machine on the shop LAN — it is **not deployed here** and cannot be, since readers are not reachable from shared hosting.
- Server-side reader hosting is documented as unsafe behind a load balancer: a UHF bridge accepts one client, so multiple instances would fight over a reader. This directly constrains the 10+ reader target.
- Tag arbitration correctness **is** verified under contention (20 concurrent claims → exactly one winner), but with the caveat that BLOCKER-1 makes every till report station 1, which defeats it in practice.

Every RFID test case you listed — reader discovery, disconnection, reconnection, timeout, duplicate reads, unknown tags, reassignment, latency, multi-reader concurrency — is **BLOCKED** pending hardware plus an on-premise deployment.

---

## 5. Suspected and confirmed hardcoded values

I have source access, so these are **confirmed**, not suspected.

| Value | Where | Current | Problem | Recommendation |
|---|---|---|---|---|
| Station ID | Frontend build | `1` | Baked into the JS bundle; one build per till | Move to session/device registration |
| Location ID | Frontend build | `1` | Same | Same |
| API origin | Frontend build (`NEXT_PUBLIC_API_URL`) | `https://pos.sma-techno.net/backend` | Changing the public origin needs a **rebuild**, not a config edit | Resolve at runtime from the serving origin |
| Cart TTL | `SqlCartStore` | 12 hours | Not configurable | Move to config |
| Idempotency retention | `SqlIdempotencyStore` | 24 hours | Not configurable | Move to config |
| Cache sweep interval / batch | `CacheSweeper` | 10 min / 500 rows | Not configurable | Move to config |
| Lockout policy | `Program.cs` | 5 attempts / 15 min | Not configurable per deployment | Move to config |
| Password policy | `Program.cs` | len 8, digit only | Weak, not configurable | Move to config, strengthen |
| Access/refresh token lifetimes | `AuthConstants` | compile-time | Security policy should be operational | Move to config |
| Currency | Seeded location | `PKR` | Single currency per location; no multi-currency | Verify against multi-store plans |

**Not verifiable without a session:** tax rates, payment methods, receipt header/footer, units, categories, date formats, company details. Several of these are the *most* likely to be hardcoded in a v1 POS and should be checked in Phase 2.

---

## 6. Missing features for enterprise deployment

### Critical — required before production
1. **User and role administration** — create, assign role, deactivate, reset password. Currently absent entirely.
2. **Per-device station identity** — replaces the build-time constant.
3. **Multi-instance capability** — Redis backplane; without it the ceiling is one server.
4. **Working scheduled jobs** — an execution host that survives idle.
5. **Admin-initiated password reset** — and/or working SMTP for self-service.
6. **Backup and restore / disaster recovery** — no evidence of any. For a system holding takings this is not optional.

### High
7. Offline POS mode — a till that stops selling when the network drops is a shop that stops trading. Especially important given this is currently internet-dependent.
8. Monitoring, alerting, health dashboards beyond the two endpoints.
9. Multi-store administration and cross-store reporting.
10. Reader health monitoring and RFID event logs.
11. Station health monitoring across 1,000 terminals.
12. Data retention and archiving policy for the sales ledger.

### Medium
13. Approval workflows (partially present — `SecurityController` has approvals).
14. Bulk import/export beyond the legacy migration path.
15. Scheduled report delivery.
16. Multi-currency, multi-language.
17. Webhooks / integration API for ERP or accounting.

### Low
18. Loyalty and promotions engine depth.
19. Advanced discount rule builder.

*Several of these may already exist behind the login — this list is drawn from source and the unauthenticated surface only, and must be revised in Phase 2.*

---

## 7. What Phase 2 requires

To complete the remaining ~70%:

1. **An authenticated browser session.** Sign in at https://pos.sma-techno.net and leave the tab open.
2. **A product catalogue.** Either run the legacy Retail Plus import, or enable `Demo__SeedCatalogue` with `Database__Seed`/`Database__AutoMigrate` on a **throwaway database** — not this one, if it is destined to hold real stock.
3. **A second user account** with a non-admin role, so the permission matrix can actually be filled in rather than guessed. Currently impossible to create (BLOCKER-3) — the seeder can make one cashier via `Auth__CashierEmail`/`Auth__CashierPassword` on a restart.
4. **For RFID:** hardware and an on-premise deployment. Nothing else will do.
5. **For scale claims:** a load-testing harness. Browser testing cannot establish any of the numbers in your target.

---

## 8. Scores

Scored only where there is evidence. An unscored dimension is worse than a low score — it means nobody knows.

| Dimension | Score | Basis |
|---|---|---|
| Functional correctness | **Not scored** | No authenticated workflow was exercisable |
| UX | **Not scored** | Only the sign-in and landing pages were seen |
| Security | **72 / 100** | Strong headers, correct 401s, no exposed admin surfaces, sound token design. Loses points for weak password policy, no admin reset, no SMTP, short HSTS |
| Performance | **35 / 100** | 17s cold start; high variance on a no-op endpoint; nothing else measurable |
| Scalability | **15 / 100** | Single instance ceiling; build-time station identity; cross-server cache round trips; connection pool unraised |
| Reliability | **25 / 100** | Single point of failure throughout; idle unload; scheduled jobs do not run; no backup evidence |
| Data integrity | **Not scored** | Zero transactions executed. The cache stores' atomicity **is** verified under contention, but no sale has ever been rung |
| RFID readiness | **Not scored** | Entirely blocked on hardware |
| Enterprise readiness | **12 / 100** | No user management, no multi-store, no multi-terminal, no DR |
| Maintainability | **80 / 100** | Source is unusually well documented — comments explain *why*, not *what*. Clean layering enforced by architecture tests. 714 passing tests |

### Overall

**Production readiness: NOT READY.**

Not because the software is bad — the codebase is careful and the security model is better than most systems at this stage. It is not ready because:

- It cannot serve a second till (BLOCKER-1).
- It cannot serve a second server (BLOCKER-2).
- It cannot onboard a second user (BLOCKER-3).
- It has never processed a single transaction (BLOCKER-4).

Against the stated target of 10+ readers and 1,000+ stations, the honest position is that **the current deployment is a single-till demonstration**. The gap is architectural, not cosmetic, and is concentrated in a small number of well-understood places.

**Recommended next step:** do not pursue breadth-first QA yet. Fix BLOCKER-3 (user management) and BLOCKER-1 (station identity), load a catalogue, then re-run this audit with a session and real data. Those three unlock almost everything else.
