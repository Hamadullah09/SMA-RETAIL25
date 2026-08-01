# Security review

**Run on 2026-08-01** against the doc-07 hardening checklist, plus a full dependency scan of both
stacks. This is a self-review by the team that wrote the code. It is not a penetration test, and
[what it cannot tell you](#what-this-review-does-not-cover) is stated at the end rather than left
implied.

## Headline

| | |
|---|---|
| .NET packages with known advisories | **0** — was 5 (2 High, 3 Moderate) |
| npm packages with known advisories | 2 High, **neither exploitable in this application** — see [the Next.js finding](#f1-nextjs-advisories-not-applicable-to-this-application) |
| Findings fixed during the review | 5 |
| Findings accepted with justification | 1 |
| Findings raised for the operator | 2 |

---

## Dependency scan

### .NET — all cleared

```bash
dotnet list package --vulnerable --include-transitive
```

Every one was **transitive** — nothing referenced them directly, which is why they had gone
unnoticed. `CentralPackageTransitivePinningEnabled` was already on, so each was fixed by adding a
`PackageVersion` to `Directory.Packages.props` rather than by chasing the parent package.

| Package | Was | Now | Severity | Advisory | Arrived under |
|---|---|---|:---:|---|---|
| `Newtonsoft.Json` | 11.0.1 | 13.0.3 | **High** | GHSA-5crp-9r3c-p9vr | Hangfire |
| `SQLitePCLRaw.*` | 2.1.6 | 2.1.12 | **High** | GHSA-2m69-gcr7-jv3q | Microsoft.Data.Sqlite (the agent's tag spool) |
| `MessagePack` | 2.5.108 | 2.5.302 | **High** | GHSA-4qm4-8hg2-g2xm, GHSA-hv8m-jj95-wg3x | SignalR's Redis backplane |
| `OpenTelemetry.*` | 1.12.0 | 1.17.0 | Moderate | GHSA-4625-4j76-fww9, GHSA-g94r-2vxg-569j, GHSA-q834-8qmm-v933 | direct |

The `Newtonsoft.Json 11.0.1` one is worth naming: a 2018 build, reachable through the job scheduler,
with a documented denial-of-service on deeply nested JSON. It had been in every build of this project
and had never been looked for.

Each is pinned to the **lowest patched version inside its existing major**. Taking the newest of each
would have meant SQLitePCLRaw 3 and MessagePack 3 — two major bumps across a serial-port spool and a
hub protocol — for no security benefit over these.

Verification after the change: `dotnet list package --vulnerable --include-transitive` reports *no
vulnerable packages* for all 14 projects, and 666 unit tests still pass.

### npm

`next` was upgraded 14.2.21 → **14.2.35**, the latest of its major, which cleared the one *critical*
advisory (GHSA-3h52-269p-cp9r, dev-server origin verification).

---

## Findings

### F1 · Next.js advisories not applicable to this application
**Severity: informational (reported as High by `npm audit`) · Accepted**

`npm audit` reports ~29 advisories against `next@14.2.35`. All remaining ones are fixed only in Next
15 or 16, which is a framework major upgrade rather than a patch.

Rather than accept or dismiss on version number alone, each advisory class was checked against what
this application actually uses:

| Advisory class | Uses it? |
|---|:---:|
| Image Optimization API (`next/image`) | **No** — 0 imports |
| Middleware (redirect, rewrite, auth bypass, SSRF) | **No** — no `middleware.ts` exists |
| Server Actions (`'use server'`) | **No** — 0 occurrences |
| i18n rewrites | **No** — not configured |
| `beforeInteractive` scripts | **No** |
| CSP nonces in App Router | **No** |

The application is an App Router SPA behind a BFF: route handlers under `/api/auth/*` and
`/api/proxy/*`, and client components. None of the vulnerable surfaces are reachable because none of
them are present.

**Accepted for now. Recommended: plan a Next 15 upgrade** as scheduled maintenance, not as an
emergency. It will need every screen re-verified, which is why it is not being done inside a security
patch.

> `postcss 8.4.31` is also flagged, and is bundled *inside* Next rather than resolvable
> independently — the top-level `postcss` is already 8.5.23. It goes away with the Next upgrade.

### F2 · The compose file ships a default administrator password
**Severity: High · Raised for the operator**

`deploy/docker-compose.yml` contains:

```yaml
- Auth__AdminPassword=${ADMIN_PASSWORD:-ChangeMeDev123!}
```

The fallback is a published credential in a tracked file, and it seeds a **full administrator**. Any
deployment brought up with `docker compose up` and no `ADMIN_PASSWORD` in the environment has a known
administrator password.

It is guarded by an environment override and the file is labelled development-only, so this is not a
code defect — but the seeder elsewhere in this codebase refuses to invent a credential *precisely*
because a seeded one is a published one, and compose quietly does the opposite.

**Action for the operator:** set `ADMIN_PASSWORD` (and `SESSION_SECRET`, same pattern on line 88)
before any deployment that is not a throwaway bench.

### F3 · `Cache:Provider=InMemory` disables cross-till arbitration
**Severity: Medium · Mitigated in code**

Running without Redis holds cart state, tag claims and hub tickets in one process. Two tills could
then sell the same tagged item, and the discrepancy would surface weeks later at a stock count.

Mitigated three ways, all added during this review's window: it is an **explicit opt-in** and never
an automatic failover on a Redis outage; it **throws at startup in Production**; and it logs a
warning naming exactly what has been given up.

### F4 · Antiforgery failure surfaced as a 500 — *fixed*
A stale sign-in form produced `server.error` with no path to recovery. Now redirects to a fresh form
with an explanation. Two integration tests added.

### F5 · Terminal agent could not authenticate — *fixed*
The agent presented a shared secret as if it were a bearer token; every call was refused, silently.
It now performs a `client_credentials` exchange. Related: a machine principal holding the terminal
scope resolves to a **narrow** permission set — read its own profile, ring tags onto its own cart. It
cannot commission a tag, void a sale, discount a line or open a drawer.

### F6 · Reset links are written to the log in Development — *by design, verify before production*
`Mail:WriteToLog` writes password-reset links to the application log when no relay is configured.
That is a credential in a log file. It is opt-in, named for what it does, logged at Warning, and
**must be off** wherever logs are retained or shipped.

---

## Doc-07 checklist

| Control | Status |
|---|:---:|
| Tokens never reach JavaScript (BFF holds them) | ✅ asserted by an e2e spec |
| Session cookie httpOnly, SameSite, Secure in production | ✅ |
| `__Host-` cookie prefix in production | ✅ environment-split, because `__Host-` cannot be satisfied over the documented plain-HTTP dev flow |
| PKCE S256 required | ✅ advertised in discovery and asserted by integration test |
| Authorization code flow refuses a request with no challenge | ✅ integration-tested |
| Permission checked server-side on every command | ✅ MediatR `AuthorizationBehavior` |
| Audit trail append-only, UPDATE/DELETE revoked in production | ✅ |
| Rate limits on token, PIN and lookup endpoints | ✅ plus the account endpoints added this session |
| Account enumeration resistance on sign-up and recovery | ✅ integration-tested |
| Password reset is single-use and rotates the security stamp | ✅ integration-tested |
| Reset token bound to one account | ✅ integration-tested — a token for one account is refused on another |
| Lockout after repeated failures | ✅ 5 attempts, 15 minutes |
| Security headers | ✅ `SecurityHeadersMiddleware` |
| CORS restricted to the BFF origin | ✅ |
| Secrets absent from tracked files | ⚠️ see [F2](#f2-the-compose-file-ships-a-default-administrator-password) |

---

## What this review does not cover

- **No third-party penetration test.** This is the authors reviewing their own work, which reliably
  misses the things the authors did not think of. An external test is a procurement item.
- **No runtime fuzzing or DAST.** Static review, dependency scanning and the existing test suite only.
- **No infrastructure review.** TLS termination, network segmentation, database hardening, backup
  encryption and OS patching are all deployment concerns and none were assessed.
- **No load or resource-exhaustion testing.** Denial-of-service resistance beyond the configured rate
  limits is unmeasured. The RFID pipeline is the one exception —
  see [RFID_Throughput_Benchmark.md](../../RFID_Throughput_Benchmark.md).

## Re-running it

```bash
dotnet list package --vulnerable --include-transitive
```

```bash
cd frontend && npm audit --omit=dev
```

Both belong in CI. A dependency scan run once is a snapshot; run on every build, it is a control.
