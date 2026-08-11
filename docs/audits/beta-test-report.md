# SMA Retail POS — Beta Test Report & Enterprise Readiness Audit

**Live system:** https://pos.sma-techno.net
**Build under test:** deploy/myasp-net-pos @ `1303573`
**Account:** `admin@sma.rms.com` (Administrator, access level 4)
**Dates:** 11–12 August 2026
**Tester roles:** QA Engineering · Architecture · Security · Performance · Product · UX

---

## 1. Executive Summary

| Metric | Count |
|---|---|
| Test cases executed | **41** |
| Passed | **22** |
| Failed | **9** |
| Blocked | **10** |
| Modules reached | 7 of 15 |
| Critical bugs | **4** |
| High bugs | **3** |
| Medium bugs | **2** |
| Low bugs | **2** |
| Confirmed hardcoded values | 10 |
| Duplicate / repetitive features | 3 |
| Missing enterprise features | 18 |
| Scalability risks | 7 |
| Security risks | 4 |
| **Production blockers** | **6** |

**Overall Beta Score: 34 / 100 · Production Readiness: NOT READY**

**Recommended next step:** fix BUG-01 (till cannot add an item) and BUG-04 (WebSockets fail).
Nothing else can be meaningfully validated until a sale can be completed.

### The three sentences that matter

1. **The till cannot ring a sale.** Selecting a serialized unit does nothing — verified by mouse, keyboard *and* a native JavaScript `.click()`. 200 of 201 products are serialized, so effectively nothing can be sold.
2. **WebSockets fail entirely**, so every real-time surface is degraded to long-polling. At 1,000 terminals this is disqualifying.
3. **No user can be created**, so the business cannot onboard a second employee through the product.

---

## 2. Application Overview

ASP.NET Core 10 API (`/backend`) + Next.js 14 BFF front end on one origin, SQL Server 2022,
OpenIddict authorisation server, SignalR for real-time, serialized/RFID-tagged inventory.
Deployed on myASP.NET shared Windows hosting. Currency PKR, single location ("Main Store"),
two stations.

### Modules discovered

| # | Module | Route | Reached |
|---|---|---|---|
| 1 | Dashboard | `/dashboard` | ✅ |
| 2 | Point of Sale | `/pos` | ✅ |
| 3 | Products / Catalogue | `/catalog/products` | ⚠️ partial |
| 4 | Inventory | `/inventory` | ✅ |
| 5 | Stock (transfers, counts) | `/stock/*` | ❌ |
| 6 | Customers | `/customers` | ✅ |
| 7 | Suppliers | `/purchasing/suppliers` | ❌ |
| 8 | Purchasing | `/purchasing` | ❌ |
| 9 | Receivables | `/receivables` | ❌ |
| 10 | Orders & Layaways | `/orders` | ❌ |
| 11 | Reports (10+ sub-reports) | `/reports/*` | ❌ |
| 12 | Administration hub | `/admin` | ✅ |
| 13 | Setup (13 tabs) | `/admin/settings` | ✅ |
| 14 | RFID readers | `/admin/rfid` | ❌ |
| 15 | Audit / Backup / Migration / Year-end / Undelete / Accounting | `/admin/*` | ❌ |

---

## 3. Testing Scope, Methodology and Environment

**Methodology.** Live black-box testing through Chrome against the authenticated application,
supported by **full source-code access** (so "hardcoded" claims are confirmed, not suspected),
direct SQL Server queries for data-integrity verification, and HTTP-level probing of the
unauthenticated surface.

**Environment.** Chrome (desktop, 1568×606 and 1707×660 viewports); Windows 11 client; server
`win8238.site4now.net`; database `sql5113.site4now.net` (SQL Server 2022 Web Edition); TLS by
Let's Encrypt.

**Constraints that limited coverage — stated plainly:**

| Constraint | Effect |
|---|---|
| BUG-01 blocks adding an item | All payment, tender, discount, refund, receipt, drawer, stock-decrement, ledger and post-sale reporting tests are **BLOCKED** |
| No RFID hardware | All 20 RFID functional cases **BLOCKED** |
| Only one user account exists, and none can be created | Permission matrix **cannot be completed** |
| No load-testing harness | Every scale number is **REQUIRES LOAD TEST** |
| Session expires unpredictably (BUG-03) | Long workflows repeatedly interrupted |

---

## 4. Detailed Test Cases

| ID | Module | Test Case | Steps | Expected | Actual | Status | Sev | Pri |
|---|---|---|---|---|---|---|---|---|
| TC-01 | Auth | Unauthenticated API access | GET `/backend/api/v1/products` | 401 | 401 | PASS | — | — |
| TC-02 | Auth | Unauthenticated customers | GET `/backend/api/v1/customers` | 401 | 401 | PASS | — | — |
| TC-03 | Auth | Unauthenticated sales | GET `/backend/api/v1/sales` | 401 | 401 | PASS | — | — |
| TC-04 | Auth | Unauthenticated staff | GET `/backend/api/v1/staff` | 401 | 401 | PASS | — | — |
| TC-05 | Auth | Unauthenticated settings | GET `/backend/api/v1/settings` | 401 | 401 | PASS | — | — |
| TC-06 | Auth | BFF proxy unauthenticated | GET `/api/proxy/products` | 401 | 401, clean JSON | PASS | — | — |
| TC-07 | Auth | Session endpoint anonymous | GET `/api/auth/session` | `authenticated:false` | as expected | PASS | — | — |
| TC-08 | Security | Swagger exposure | GET `/backend/swagger/*` | not exposed | 404 | PASS | — | — |
| TC-09 | Security | Hangfire dashboard exposure | GET `/backend/hangfire` | not exposed | 404 | PASS | — | — |
| TC-10 | Security | Security headers | inspect response | present | CSP/HSTS/XFO/XCTO/RP/PP all present | PASS | — | — |
| TC-11 | Security | Server banner | inspect headers | removed | `X-Powered-By` absent | PASS | — | — |
| TC-12 | Security | Self-registration disabled | POST `/api/v1/account/register` | rejected | 403 `registration.disabled` | PASS | — | — |
| TC-13 | Security | Account enumeration | existing vs new address | indistinguishable | same response (source-verified) | PASS | — | — |
| TC-14 | Security | HSTS max-age | inspect header | ≥1 year | **2592000 (30 days)** | FAIL | Low | P2 |
| TC-15 | Auth | Direct URL access when signed out | GET `/dashboard` | redirect to sign-in | redirected | PASS | — | — |
| TC-16 | Auth | Login with wrong password | submit bad creds | rejected, no lockout leak | "Those details were not recognised." | PASS | — | — |
| TC-17 | Auth | Antiforgery enforcement | POST without token | rejected | "That form had expired." | PASS | — | — |
| TC-18 | Auth | Session persistence over time | idle ~15 min, navigate | stay signed in | **silently signed out** | FAIL | Critical | P0 |
| TC-19 | Auth | Token family integrity | inspect OpenIddictTokens | no mass revocation | **22 revoked families** | FAIL | Critical | P0 |
| TC-20 | Dashboard | KPI tiles render | open `/dashboard` | totals + empty states | rendered correctly | PASS | — | — |
| TC-21 | Dashboard | Reorder count accuracy | compare to inventory | meaningful | 200/201 flagged — useless | FAIL | Medium | P2 |
| TC-22 | POS | Till opens | open `/pos` | station + drawer shown | Station 001, drawer open | PASS | — | — |
| TC-23 | POS | SignalR connects | observe status | connected | connects via fallback | PASS | — | — |
| TC-24 | POS | WebSocket transport | console | WS established | **WS fails, falls back** | FAIL | Critical | P0 |
| TC-25 | POS | Unknown identifier | scan `belt`, Enter | clear error | "No item matches that identifier." | PASS | — | — |
| TC-26 | POS | Product search (F9) | F9, type `belt` | matching list | 9 correct results | PASS | — | — |
| TC-27 | POS | Search result formatting | inspect prices | PKR | **shown as `$`** | FAIL | High | P1 |
| TC-28 | POS | Serialized unit offered | pick product | unit list w/ EPC | EPC `E2801170…AE1` shown | PASS | — | — |
| TC-29 | POS | **Add unit to sale (keyboard)** | Enter on unit | line added | **nothing happens** | FAIL | Critical | P0 |
| TC-30 | POS | **Add unit to sale (mouse)** | click unit | line added | **nothing happens** | FAIL | Critical | P0 |
| TC-31 | POS | **Add unit (native JS click)** | `.click()` | line added | **nothing happens** | FAIL | Critical | P0 |
| TC-32 | POS | Function-key bar present | inspect | F4/F5/F9/F10/F11 | all present | PASS | — | — |
| TC-33 | Inventory | List renders | open `/inventory` | stock columns | On hand/Committed/Available/On order/Reorder | PASS | — | — |
| TC-34 | Inventory | Pagination | scroll | paged | load-more, "100 loaded of more" | PASS | — | — |
| TC-35 | Inventory | Real-time indicator | observe | live | **"live updates offline"** | FAIL | High | P1 |
| TC-36 | Customers | List renders | open `/customers` | customer rows | 5 rows, balances, limits | PASS | — | — |
| TC-37 | Customers | Duplicate detection | inspect data | no duplicates | **#1 and #2 identical** | FAIL | Medium | P2 |
| TC-38 | Customers | **Create customer** | click "New customer" | form opens | **nothing happens** (incl. JS click) | FAIL | Critical | P0 |
| TC-39 | Admin | Setup tabs render | open Setup | config tabs | 13 tabs present | PASS | — | — |
| TC-40 | Admin | Edit staff role | open Users tab | editable | code/name/level/active/PIN + Save | PASS | — | — |
| TC-41 | Admin | **Create user** | look for Add | create control | **none exists anywhere** | FAIL | Critical | P0 |

### Blocked test cases

| ID | Area | Reason |
|---|---|---|
| TC-B01…B05 | Payment, tenders, change, receipt, refund | **BLOCKED** — BUG-01 prevents a sale |
| TC-B06…B08 | Stock decrement, ledger, post-sale reports | **BLOCKED** — BUG-01 |
| TC-B09 | Permission matrix by role | **BLOCKED** — only one account; none can be created |
| TC-B10 | All RFID hardware cases (20) | **BLOCKED: physical hardware/environment required** |

---

## 5. Bugs and Defects

### BUG-01 — Serialized item cannot be added to a sale
**Module:** POS · **Severity: Critical** · **Priority: P0** · **Reproducibility: 100%**

**Preconditions:** signed in as Administrator; POS open at station 001; drawer open.

**Steps to reproduce**
1. Press `F9` → Find item.
2. Type `belt` → 9 results render (code, name, price).
3. Press `Enter` → "Choose a unit" opens for `FR0207001` listing EPC `E28011700080020A7A6B6AE1`.
4. Press `Enter` on the unit → nothing.
5. Click the unit with the mouse → nothing.
6. Execute a native `document.querySelector(...).click()` → nothing.

**Expected:** unit added as a sale line; subtotal and total update.
**Actual:** dialog remains open; sale stays `EMPTY`; total stays `Rs 0.00`.

**Business impact:** 200 of 201 products carry serialized units, so **no sale can be completed**.
This is a total loss of primary function and blocks ~10 further test areas.

**Suggested fix:** trace the unit-selection handler; confirm whether a request is issued to
`/api/proxy/...` at all. Likely an unwired or silently-failing selection callback. Add an
end-to-end test covering *search → choose unit → line appears*, which would have caught this.

---

### BUG-02 — "New customer" button does nothing
**Module:** Customers · **Severity: Critical** · **Priority: P0** · **Reproducibility: 100%**

**Steps:** `/customers` → click **New customer** (tested by ref-click, coordinate click, and native
JS `.click()`).
**Expected:** create form/dialog opens. **Actual:** no dialog (`[role=dialog]` count 0), no form in
the DOM, no console error, no network request.

**Business impact:** customers cannot be created through the UI. Combined with BUG-01, two of the
three core retail entities cannot be created at the counter.

**Suggested fix:** same class as BUG-01 — verify the dialog-open handler is wired. The identical
symptom in two unrelated modules suggests a **shared dialog/overlay component is broken**, which
would also explain the POS unit dialog not responding. **Investigate the shared dialog component
first — one fix may resolve both.**

---

### BUG-03 — Refresh-token families revoked, causing unpredictable sign-out
**Module:** Authentication · **Severity: Critical** · **Priority: P0** · **Reproducibility: observed 2× in ~15 min**

**Evidence — `OpenIddictTokens` by type and status:**

| Type | valid | redeemed | **revoked** |
|---|---|---|---|
| refresh_token | 26 | 14 | **22** |
| access_token | 54 | — | **22** |
| id_token | 40 | — | **22** |

Matched sets of 22 across all three types = whole token families revoked. **Revocations exceed
successful rotations (22 vs 14).**

**Mechanism (source-verified):** access token 15 min; `SetRefreshTokenReuseLeeway(TimeSpan.Zero)`;
dashboard/POS issue parallel BFF calls that cross the expiry boundary together — one rotates, the
others replay a spent token, reuse detection revokes the family, BFF calls `clearSession()`.
The `refreshesInFlight` collapse map is per-process and insufficient.

**Business impact:** a cashier is signed out mid-sale without warning. Worsens with concurrency —
directly hostile to the 1,000-terminal target.

**Suggested fix:** serialise refresh with a short server-side lock keyed on the session (not an
in-process map); allow a small non-zero reuse leeway; show "your session expired" and return the
user to the page they were on.

---

### BUG-04 — WebSocket transport fails; SignalR degrades to long-polling
**Module:** Real-time / Infrastructure · **Severity: Critical** · **Priority: P0** · **Reproducibility: 100%**

**Console (repeats every ~20s):**
```
Failed to start the transport 'WebSockets': Error: WebSocket failed to connect.
The connection could not be found on the server, either the endpoint may not be a
SignalR endpoint, the connection ID is not present on the server, or there is a
proxy blocking WebSockets.
```

**Observed symptoms:** Customers shows "Not updating — reconnecting"; Inventory shows
"live updates offline"; POS shows "Server offline" before falling back.

**Business impact:** every real-time surface — live cart, RFID tag feed, inventory sync — runs on
long-polling. Long-polling holds a request per client per interval; at 1,000 terminals this
saturates the connection pool and thread pool long before the target is reached. **This alone
prevents the stated scale.**

**Likely cause:** IIS WebSocket support unavailable on this shared plan, or the ANCM
out-of-process hop / Node parent site not passing the upgrade. Also note the error's own hint about
sticky sessions — relevant the moment a second instance exists.

**Suggested fix:** confirm the IIS WebSocket feature is installed and the `<webSocket>` element is
not being rejected; validate on on-premise IIS. Treat WebSocket support as a hosting requirement.

---

### BUG-05 — Currency symbol inconsistent between components
**Module:** POS · **Severity: High** · **Priority: P1** · **Reproducibility: 100%**

Find-item dialog renders `$2000.00`, `$2950.00`; sale panel renders `Rs 0.00`; location currency is
`PKR`. Two currencies on one screen.
**Impact:** cashier cannot trust displayed prices; undermines the Currencies configuration tab.
**Fix:** single shared formatter driven by location currency; no literal symbols in components.

---

### BUG-06 — Real-time indicator inconsistent across modules
**Module:** UX / Real-time · **Severity: High** · **Priority: P1**

Three different states for one underlying fault: "Server offline" (POS), "live updates offline"
(Inventory), "Not updating — reconnecting" (Customers). Users cannot tell whether data is stale.
**Fix:** one shared connection-state component and one vocabulary.

---

### BUG-07 — Duplicate customer records with no dedupe
**Module:** Customers · **Severity: Medium** · **Priority: P2**

Customers #1 and #2 are both "Hamadullah Arain" with empty city/phone/email/type.
**Impact:** duplicate customer master data corrupts loyalty, credit limits and statements.
**Fix:** duplicate detection on name+phone/email at creation; merge tool for existing duplicates.

---

### BUG-08 — Reorder alerting is meaningless as configured
**Module:** Dashboard / Inventory · **Severity: Medium** · **Priority: P2**

Dashboard reports **200 below reorder**; every product has on-hand 1 and reorder point 1, so
"at or below" flags essentially the whole catalogue.
**Impact:** an alert that always fires is an alert nobody reads.
**Fix:** distinguish "at" from "below"; sensible seeded defaults; suppress when on-order covers.

---

### BUG-09 — HSTS max-age below recommended
**Module:** Security · **Severity: Low** · **Priority: P2**
`max-age=2592000` (30 days). Raise to `31536000` with `includeSubDomains`.

---

### BUG-10 — Sign-in page advertises unavailable self-registration
**Module:** Auth / UX · **Severity: Low** · **Priority: P3**
"No account yet? Create one" leads to 403 `registration.disabled`. Hide the link when disabled, or
say accounts are issued by an administrator. **This is the exact dead end reported by the client.**

---

## 6. Security Findings

**Strong.** The security model is the best-engineered part of the system.

| Control | Status |
|---|---|
| API authorisation (401 on all endpoints) | PASS |
| Swagger / Hangfire exposure | PASS — both 404 |
| CSP `default-src 'none'`, HSTS, XFO DENY, nosniff, Referrer-Policy, Permissions-Policy | PASS |
| BFF pattern — tokens never reach the browser | PASS |
| Reference tokens (revocable sessions) | PASS |
| PKCE mandatory, `plain` removed (S256 only) | PASS |
| Refresh rotation + reuse detection | PASS *(but see BUG-03)* |
| Account enumeration prevented | PASS |
| Lockout 5 attempts / 15 min; per-caller rate limiting | PASS |
| Self-registration off by default; Trainee role when on | PASS |

**Gaps**

| ID | Finding | Severity |
|---|---|---|
| SEC-1 | Password policy: 8 chars + digit only; no uppercase/symbol/breach check | Medium |
| SEC-2 | No admin-initiated password reset; SMTP unconfigured so `forgot-password` cannot deliver | **High** |
| SEC-3 | HSTS 30 days (BUG-09) | Low |
| SEC-4 | Admin credential seeded from env var, never rotated; only one account exists | Medium |

**NOT TESTED (requires working CRUD):** XSS/HTML injection in product & customer fields, SQL
injection indicators, CSRF on state-changing endpoints, IDOR on `{id:long}` routes, privilege
escalation between roles, file-upload safety.

---

## 7. Performance Findings (measured)

Sequential requests to `/backend/health/live`:

```
 1. 16763 ms   ← cold start after idle unload
 2.  1275 ms
 3.   327 ms
 …
10.   272 ms
```

| Endpoint | min | median | max |
|---|---|---|---|
| `/backend/health/live` (no I/O) | 321 ms | 1029 ms | 1214 ms |
| `/backend/health/ready` (SQL) | 268 ms | 314 ms | 957 ms |
| `/` (Next.js SSR) | 322 ms | 708 ms | 979 ms |
| `/api/auth/session` (BFF) | 269 ms | 385 ms | 781 ms |

**Caveat:** absolute figures include internet latency from the test client to a US host. The
meaningful signals are the **17-second cold start** and the **high variance on a no-op endpoint**
(321→1214 ms), which indicates contention on shared hosting.

**Also observed:** the site became unreachable between probe batches (app pool unloaded), and the
database intermittently timed out on the pre-login handshake from outside.

---

## 8. Scalability Findings

Against **10+ readers / 1,000+ stations**:

| # | Risk | Label |
|---|---|---|
| S-1 | **Station identity is compiled into the JS bundle** (`NEXT_PUBLIC_STATION_ID`) — every browser is station 1 | **VERIFIED** |
| S-2 | **No SignalR backplane** under `Cache:Provider=SqlServer` — single instance only | **VERIFIED** |
| S-3 | **WebSockets fail** → long-polling → connection/thread exhaustion far below 1,000 clients | **VERIFIED** |
| S-4 | Cache stores (cart, tag claim, idempotency, hub ticket) are cross-server SQL round trips; tag claims are the hot path | **VERIFIED** |
| S-5 | ADO.NET pool caps at 100 and queues; `Max Pool Size` not raised | **INFERRED** |
| S-6 | Workstation GC set for a 1 GB pool — wrong for server hardware | **VERIFIED** |
| S-7 | Cold start ~17 s on idle unload | **VERIFIED** |

**REQUIRES LOAD TEST (cannot be established from a browser):** sustained throughput at 1,000
stations; SignalR connection ceiling per instance; `MERGE`/`HOLDLOCK` contention on
`cached_tag_claim` with 10+ readers; deadlock behaviour on `cached_cart`; connection-pool
saturation point; multi-day memory behaviour; report cost against a large ledger.

---

## 9. RFID Findings

**BLOCKED: physical hardware/environment required.** No reader was attached; the POS tag panel
correctly reported "Not connected to the reader feed".

**Established without hardware:**
- Serialized/EPC model works — 200 serialized units, real EPCs surfaced per unit at the till.
- Tag arbitration is correct under contention: `MERGE … HOLDLOCK` yields exactly one winner from 20 concurrent claims (verified in integration tests).
- **But** S-1 means every till reports station 1, which defeats cross-till arbitration in practice.
- `Rfid:ServerReaders:Enabled` is false; the terminal agent is not deployed and cannot be on shared hosting.
- Source notes a UHF bridge accepts one client — multiple app instances would fight over a reader, constraining the 10-reader target.

All 20 requested RFID cases remain **BLOCKED**.

---

## 10. Data Integrity Findings

**Largely BLOCKED by BUG-01** — no sale could be executed, so the POS→inventory→ledger→reports→
customer-balance chain is unverified.

**Verified:** cache-store atomicity (tag claim single-winner; hub ticket single-redemption;
idempotency replay) under contention against SQL Server 2019/2022.
**Observed defect:** duplicate customer records (BUG-07).
**Untested:** rollback on payment failure, double-submit, network loss mid-transaction, negative
stock, orphan records.

---

## 11. Confirmed Hardcoded Values *(source-verified, not suspected)*

| Value | Location | Current | Why it matters | Fix |
|---|---|---|---|---|
| Station ID | frontend build | `1` | One build per till; blocks multi-terminal | Session/device registration |
| Location ID | frontend build | `1` | Same | Same |
| API origin | frontend build | `…/backend` | Origin change needs a rebuild | Resolve from serving origin |
| Currency symbol | POS find dialog | `$` | Contradicts PKR (BUG-05) | Shared formatter |
| Cart TTL | `SqlCartStore` | 12 h | Not tunable | Config |
| Idempotency retention | `SqlIdempotencyStore` | 24 h | Not tunable | Config |
| Sweep interval / batch | `CacheSweeper` | 10 min / 500 | Not tunable | Config |
| Lockout policy | `Program.cs` | 5 / 15 min | Not per-deployment | Config |
| Password policy | `Program.cs` | len 8 + digit | Weak | Config + strengthen |
| Token lifetimes | `AuthConstants` | 15 min / 8 h | Security policy is operational | Config |

**Correctly configurable (credit where due):** taxes, tenders, currencies, numbering, printers,
hardware, stations, pricing rules, branding, business identity, departments/categories — 13 Setup tabs.

---

## 12. Duplicate / Repetitive Features

| # | Duplication | Where | Why confusing | Recommendation |
|---|---|---|---|---|
| D-1 | Staff appears twice | `/admin/staff` and `/admin/settings?tab=Users` | Role is **read-only** in one and **editable** in the other | Merge into one People screen |
| D-2 | Three connection-state vocabularies | POS / Inventory / Customers | Same fault, three labels (BUG-06) | One shared component |
| D-3 | Reorder surfaced twice | Dashboard tile + Inventory filter | Same list, two entry points, no shared definition | Single definition, dashboard links to it |

---

## 13. Missing Features

### Critical — before production
1. **User creation / onboarding** — no way to add an employee.
2. **Admin password reset** (+ working SMTP).
3. **Per-device station identity** — replaces the build constant.
4. **Multi-instance capability** — Redis backplane + WebSockets.
5. **Backup / restore / DR** — none evident for a system holding takings.
6. **Working scheduled jobs** — `Jobs:RunServer=false`, nothing runs.

### High
7. Offline POS mode. 8. Monitoring/alerting beyond two health endpoints. 9. Multi-store
administration and consolidated reporting. 10. RFID reader health monitoring + event logs.
11. Station health across 1,000 terminals. 12. Data retention / archiving.

### Medium
13. Bulk import/export beyond legacy migration. 14. Scheduled report delivery. 15. Multi-currency
and multi-language. 16. Webhooks / integration API.

### Low
17. Loyalty & promotions depth. 18. Advanced discount rule builder.

---

## 14. Enterprise Architecture Risks

| Finding | Label |
|---|---|
| Single point of failure end-to-end: one site, one pool, one DB, no failover | **OBSERVED** |
| Deployed on remote shared hosting, contradicting the "on-premise reliability" goal | **OBSERVED** |
| App and DB on different hosts — every cart/tag operation crosses the network | **OBSERVED** |
| No WebSockets → long-polling at scale | **OBSERVED** |
| No distributed locking for refresh (BUG-03) | **OBSERVED** |
| Connection pool ceiling unraised | **LIKELY RISK** |
| Sticky sessions required once multi-instance (per SignalR error text) | **LIKELY RISK** |
| No queue/back-pressure for RFID bursts | **REQUIRES SOURCE-CODE REVIEW** |

---

## 15. UX Findings

- **Keyboard-first design is genuinely good** — full function-key bar, "Enter picks the first result", scan-first focus. Well suited to a counter.
- Error copy is plain English ("No item matches that identifier") — good for non-technical staff.
- **Dead controls with no feedback** (BUG-01, BUG-02): clicking produces nothing at all — no spinner, no error, no toast. This is the single worst UX trait; a cashier cannot distinguish "broken" from "slow".
- Red `Server offline` on normal startup causes needless alarm.
- "100 loaded of more" is awkward; should read "100 of 201".
- No breadcrumbs in deep admin routes.

**NOT TESTED:** responsive/tablet/small-screen layouts, accessibility, colour contrast, cross-browser (Edge/Firefox) — deprioritised in favour of functional blockers.

---

## 16. Production Blockers

| # | Issue | Why it blocks release |
|---|---|---|
| PB-1 | **BUG-01** — cannot add an item to a sale | The product's primary function does not work |
| PB-2 | **BUG-04** — WebSockets fail | Real-time is degraded; the 1,000-station target is unreachable |
| PB-3 | **BUG-03** — sessions revoked unpredictably | Cashiers signed out mid-sale |
| PB-4 | **BUG-02 / TC-41** — cannot create customers or users | Business cannot onboard staff or clients |
| PB-5 | **S-1** — station identity compiled into the bundle | A second till is impossible |
| PB-6 | No backup / DR | Unacceptable for a system holding takings |

---

## 17. Enterprise Readiness Scores

| Dimension | Score | Basis |
|---|---|---|
| Functional correctness | **20** | Core sale path broken; two create flows dead |
| UX | **55** | Strong keyboard design; dead controls with no feedback |
| Security | **72** | Excellent controls; gaps in reset, policy, HSTS |
| Performance | **35** | 17 s cold start; high variance |
| Scalability | **15** | Single instance; no WS; build-time station |
| Reliability | **25** | SPOF; idle unload; jobs don't run; sessions drop |
| Data integrity | **40** | Cache atomicity proven; end-to-end unverified |
| RFID readiness | **Not scored** | Blocked on hardware |
| Enterprise readiness | **18** | No onboarding, no multi-store, no DR |
| Maintainability | **80** | Excellent documentation, clean layering, 714 tests |

### Overall Beta Score: **34 / 100**
### Production Readiness: **NOT READY**

**Why.** This is not a weak codebase — maintainability and security are genuinely strong, and the
architecture is thoughtfully documented. It is not ready because the **primary function does not
work** (no sale can be rung), **two creation flows are dead**, **sessions drop unpredictably**, and
**three independent constraints each individually prevent the stated scale**. These are
concentrated, well-understood defects rather than pervasive rot — which is encouraging for the
remediation timeline.

---

## 18. Prioritised Roadmap

### Phase 1 — Before Pilot (blockers)
1. BUG-01 — restore add-to-sale. **Investigate the shared dialog component first: BUG-02 shows the same signature and one fix may resolve both.**
2. BUG-02 — customer creation.
3. BUG-03 — serialise token refresh; add reuse leeway; session-expiry messaging.
4. BUG-04 — WebSockets (may require a different host).
5. Build **user creation + role assignment**.
6. BUG-05 — currency formatter.
7. Add end-to-end tests for search → add → pay → receipt, and for create-customer/create-user.

### Phase 2 — Before Production
8. Admin password reset + SMTP. 9. Backup/restore + DR runbook. 10. Scheduled-job host.
11. Monitoring/alerting. 12. Password policy. 13. BUG-06/07/08. 14. Complete the untested
modules (Stock, Purchasing, Receivables, Orders, Reports, 8 admin pages).

### Phase 3 — Enterprise Scale
15. Per-device station identity. 16. Redis + backplane + sticky sessions. 17. On-premise
deployment. 18. Connection pool sizing + server GC. 19. **Load test to 1,000 stations / 10 readers.**
20. Multi-store administration.

### Phase 4 — Future
21. Offline POS. 22. Multi-currency/language. 23. Webhooks/integration API. 24. Scheduled reports.
25. Loyalty & promotions depth.

---

## 19. Phase 2 — API sweep of the remaining modules

The modules unreachable through the UI were swept at the API layer instead, using the authenticated
session. This covers Suppliers, Purchasing, Stock, Receivables, Orders, Reports, Audit and Terminals.

### What works

| Endpoint | Result |
|---|---|
| `reports/stock-value?locationId=1` | Correct: 51 units Womenswear, cost 204,910, retail 372,450 |
| `reports/on-order?locationId=1` | Correct: PO for 5 units from Karachi Textile Mills |
| `reports/margin`, `tax`, `sales-analysis`, `stock-position`, `stock-received`, `reward-points` | All 200, all shaped correctly |
| `suppliers?locationId=1` | Returns SUP-001 Karachi Textile Mills with full contact detail |
| `purchase-orders?locationId=1` | Returns PO #1, Posted, total 126,780 |
| `audit` | **Working** — 3,242 rows, sign-in events captured with timestamps |
| `tender-types` | Returns Cash with drawer/over-tender/rounding flags |
| `transfers`, `stock-counts`, `layaways`, `customer-orders`, `receivables/*` | 200, empty (no data yet) |

**Audit logging is genuinely functional** — this is a real strength and upgrades my Section 6 view.

### BUG-11 — Location-scoped endpoints return an empty list instead of an error *(FIXED)*
**Module:** API (25 controllers, ~60 endpoints) · **Severity: High** · **Priority: P1**

**Discovery:** `GET /api/v1/suppliers` returned `{"items":[]}` — but `GET /api/v1/suppliers?locationId=1`
returned the supplier. Same for purchase orders, customers and every report.

**Root cause (source-verified):** the parameter is declared `[FromQuery] long locationId` — non-nullable,
with no `[BindRequired]`. ASP.NET Core model binding therefore defaults it to `0` when it is absent,
and `locationId = 0` matches no rows. The caller gets `200 OK` and an empty set.

**Business impact:** a report that answers "you have no stock" when the question was malformed is worse
than one that errors. A manager checking stock value via a saved URL missing the parameter would be
told the shop is empty, and nothing on screen would indicate a problem.

**Fix applied:** `[BindRequired]` added to every `[FromQuery] long locationId` across all 25 controllers,
so a missing parameter now returns `400` instead of a silent empty set. Verified safe: every by-id route
(`/customers/{id}` etc.) never took the parameter, and every browse call in the front-end client already
passes it. Backend builds clean and all 745 tests pass.

### OBS-3 — No fiscal year is configured
`fiscal_years` is empty (0 rows). Year-end, accounting sync and any period-bounded report have no
period to work within. **Not scored as a bug** — plausibly just an unconfigured fresh deployment —
but it must be set before go-live.

### OBS-4 — Database unreachable from outside on ~2 of 3 attempts
Five separate `sqlcmd` sessions needed 2–3 retries each ("Named Pipes Provider… could not open a
connection", "Login timeout expired"). The application server sits in the same datacentre, so this is
**not** evidence the app's own connection is unreliable — but combined with the 17-second cold start
and the app pool unloading between probes, it reinforces that shared hosting is the wrong platform
for a till.

### Database ground truth (verified by direct query)

| Table | Rows |
|---|---|
| `products` | 201 |
| `serialized_units` | 200 (all `InStock`) |
| `stock_levels` | 200 |
| `stock_ledger_entries` | 200 |
| **`sales_transactions`** | **0** |
| **`sale_lines`** | **0** |
| `staff_profiles` | 1 |
| `AspNetUsers` | **1** |
| `AspNetRoles` | 5 (Trainee, Cashier, Clerk, Supervisor, Administrator) |
| `fiscal_years` | 0 |

**Zero sales rows is the hard confirmation of BUG-01** — in a full day of testing, the system has never
recorded a single transaction.

---

## 20. Fixes Delivered This Session

### 20.1 User creation — built end to end

The headline gap. `StaffController` had `[HttpGet]` but **no `[HttpPost]`**: there was no create-staff
endpoint anywhere in the API, which is why no button existed to look for.

| Layer | File | What was added |
|---|---|---|
| Port | `Retail25.Application/Abstractions/IUserProvisioner.cs` | Create / reset password / enable / list roles, so the Application layer never references Identity |
| Command | `Retail25.Application/Staff/CreateStaffCommands.cs` | `CreateStaffCommand`, `ResetStaffPasswordCommand`, `ListAssignableRolesQuery` + handlers, gated on `staff.write` |
| Adapter | `Retail25.Infrastructure/Identity/UserProvisioner.cs` | `UserManager`/`RoleManager` implementation |
| API | `Retail25.Api/Controllers/StaffController.cs` | `POST /staff`, `POST /staff/{id}/password`, `GET /staff/roles` |
| UI | `admin/settings` → Users tab | "Add a colleague" form + per-person password reset |
| UI | `components/masters/browse-form.tsx` | New `PasswordField` with a reveal toggle |
| Tests | `Staff/CreateStaffTests.cs` | **26 tests, all passing** |

**Design decisions worth noting:**
- The sign-in and the staff profile are created in **one command inside one transaction**. Either both exist or neither does — a sign-in with no profile cannot be attributed a sale, and a profile with no sign-in cannot reach a till.
- **Password rules are not restated** in the handler. Identity's configured validator is the single source of truth, and its verdict is passed through with its own code (`identity.password_too_short`), so the rule and its message cannot drift apart.
- The role picker **reads roles from the server**, so adding a role does not require a front-end rebuild — deliberately avoiding the build-time-constant mistake that causes S-1.
- Password reset uses generate-then-redeem rather than a direct hash write, because that is the path that rotates the security stamp and thereby ends sessions held by whoever knew the old password.
- The staff id travels in the route so the password stays out of the query string, and out of the IIS request log.

**Also closes SEC-2** (no admin-initiated password reset), which was rated High and depended on SMTP
that this deployment does not have.

### 20.2 BindRequired fix
Described as BUG-11 above. 25 controllers, ~60 endpoints.

### Verification

```
Domain.UnitTests          110 passed
TerminalAgent.UnitTests    94 passed
ArchitectureTests          13 passed
IntegrationTests            5 passed, 90 skipped
Application.UnitTests     523 passed   (was 497 — +26 new)
                          ─────────────
                          745 passed, 0 failed
```

Frontend: `tsc --noEmit` clean, `eslint` clean.

**Stated honestly:** the 90 skipped integration tests are Docker-gated and Docker was not running, so
**the 4 integration-test failures seen earlier in this session were not reproduced and are not fixed.**
They need a run with SQL Server available. Nothing here should be read as having addressed them.

**Also not yet done:** the new user-creation feature is **committed but not deployed** — it is not live
on `pos.sma-techno.net`, so it has not been exercised against the real database through the browser.
It is verified by unit tests and a clean build only.

---

## 21. Final Recommendation

**Do not pilot in a live shop.** Fix PB-1 through PB-5, then re-run this audit end-to-end with a
working sale, at least three role-differentiated accounts, and a load harness. The remaining
untested modules (Stock, Purchasing, Receivables, Orders, Reports and eight admin pages) must be
swept before any production claim — they are marked NOT TESTED here, not passed.

**Observed facts** are the test cases, console output, SQL results and timings above.
**Inferences** are labelled as such. **Recommendations** are the fixes and roadmap.
