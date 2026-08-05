# 07 — Security & Identity

## Topology

```
Browser ──(1) /login──► Next.js BFF ──(2) authorize + PKCE──► OpenIddict (Retail25.Api)
   │                        │                                       │
   │◄─(6) httpOnly cookie───┤◄──(5) tokens (back channel)───────────┤
   │                        │
   └─(7) fetch /api/… ──────► BFF route handler ──(8) Bearer──► API endpoints
                             (reads cookie, attaches access token, refreshes silently)
```

**No token ever reaches JavaScript.** The Next.js server holds the session; the browser holds only
an httpOnly, Secure, SameSite=Lax session cookie. This satisfies the brief's "no JWTs in
localStorage" requirement and removes XSS token theft as a class.

### Flow detail

1. `GET /login` → BFF generates `code_verifier` (43–128 chars, crypto-random), `code_challenge =
   BASE64URL(SHA256(verifier))`, `state`, `nonce`; stores them in a short-lived encrypted cookie.
2. Redirect to `/connect/authorize?response_type=code&code_challenge_method=S256&…`.
3. User authenticates against ASP.NET Core Identity (password, or PIN for POS fast-switch).
4. Redirect to `/callback` with `code`.
5. BFF exchanges code + `code_verifier` at `/connect/token` **server-side**; PKCE is *required* —
   OpenIddict is configured to reject any code exchange without a verifier, and public clients
   cannot use client secrets.
6. BFF writes an encrypted session (access + refresh token, expiry) into an httpOnly cookie
   (`__Host-r25.session`, `Secure`, `SameSite=Lax`, `Path=/`).
7. Client calls go to same-origin `/api/*` BFF routes.
8. BFF attaches `Authorization: Bearer`, refreshes on 401 once, then re-auths.

Access token lifetime **15 min**; refresh token **8 h**, rotating with reuse detection (a replayed
refresh token revokes the family). SignalR connects with a short-lived hub token minted by the BFF
via `accessTokenFactory`; hub authorization re-validates on every reconnect.

## OpenIddict configuration outline

```
Clients
  retail25-web        public, code+PKCE required, redirect https://<host>/callback,
                      scopes: openid profile roles offline_access retail25.api
  retail25-agent      confidential, client_credentials, scope retail25.terminal,
                      one credential per station (rotatable), IP-pinned where possible
Scopes                retail25.api, retail25.terminal
Certificates          dev: ephemeral; prod: X.509 signing + encryption certs from disk/KeyVault,
                      rotated annually with overlap
Endpoints             /connect/authorize /connect/token /connect/logout
                      /connect/userinfo /connect/introspect /.well-known/openid-configuration
Validation            local (same process) with reference tokens for revocability
```

## Authorization model — legacy levels → permissions

The legacy system has five levels (guide p.82). We keep them as **preset roles** so existing staff
mappings import cleanly, but authorize on fine-grained permissions.

| Legacy level | Role | Meaning (guide) |
|---|---|---|
| 0 | `Trainee` | POS practice only, **nothing is saved** |
| 1 | `Cashier` | POS sales only |
| 2 | `Clerk` | add/edit in database screens; no deletes, no void authorization |
| 3 | `Supervisor` | everything except user management and program setup |
| 4 | `Administrator` | unrestricted |

Permission catalogue (seeded, extensible):

```
pos.sell  pos.discount  pos.price_override  pos.tax_override  pos.void_sale
pos.suspend  pos.recall  pos.unknown_item  pos.reprint  pos.select_price_level
drawer.open_float  drawer.pay_in  drawer.pay_out  drawer.pop  drawer.close
catalog.read  catalog.write  catalog.delete  catalog.bulk_adjust
inventory.adjust  inventory.receive  inventory.transfer  inventory.count  inventory.year_end
customer.read  customer.write  customer.delete
ar.read  ar.payment  ar.void_invoice  ar.refund  ar.late_charges
purchasing.read  purchasing.write  purchasing.post_order  purchasing.post_shipment
staff.read  staff.write  staff.time_clock_edit
reports.sales  reports.financial  reports.cost_visibility
settings.read  settings.write  settings.taxes  settings.hardware
users.manage  migration.run  sync.run  audit.read
```

Enforced in three places, all required:
`[RequiresPermission("pos.void_sale")]` on the MediatR request → checked by `AuthorizationBehavior`;
endpoint policies on the route group; UI affordances hidden client-side (convenience only, never
trusted).

### Step-up (supervisor override)

Legacy behaviour: *"When voiding a sale you may be asked for a supervisor's password"* (p.11, p.82).
Modern form:

- Sensitive commands return `428 sale.requires_supervisor` with an `approvalRequestId`.
- The cashier either enters a supervisor PIN inline, **or** a supervisor approves from any station
  (broadcast on `PosHub.SupervisorApprovalRequested`) — better than walking to the till.
- Approval mints a single-use, 2-minute, action-scoped grant recorded in `AuditLogEntry` with both
  actor and approver.

### POS fast user switching

Cashiers cannot type a full password between customers. `StaffProfile.Pin` (Argon2id, per-user
salt, rate-limited, lockout after *N* failures) authenticates a *station-scoped* session switch
within an already-authenticated station session. Badge/RFID staff cards are a drop-in alternative
via the same endpoint. `Track Staff Sales Or Commissions` (p.82) forces a PIN before each sale.

## Data protection & PCI

- **We never touch a PAN.** Semi-integrated terminal + tokenization; we store `last4`, brand,
  `authCode`, `gatewayReference` only. This keeps the deployment at SAQ-C/P2PE rather than SAQ-D.
- Gift card numbers hashed at rest; only the last 4 displayed.
- TLS 1.2+ everywhere including the LAN. Self-signed is *not* acceptable for the station↔server hop —
  the deploy ships an internal CA or ACME setup.
- Secrets from environment/Key Vault; none in the repo. SQL Server, Redis and Hangfire all password-
  protected and bound to the internal network.
- SQL Server at-rest encryption via TDE or volume encryption; `ENCRYPTBYKEY` for the few PII columns that
  need column-level protection (customer email/phone are *not* among them — they must be searchable).
- Sensitive settings (gateway keys, accounting client secrets) encrypted with ASP.NET Core Data
  Protection keys persisted to the DB and protected by a certificate.

## Audit

Every command that mutates money, stock, price, tax or permissions writes an `AuditLogEntry`:
actor, station, IP, action, entity type/id, before/after JSONB diff, correlation id, approver (if a
step-up occurred). Append-only (revoked UPDATE/DELETE at the role level), partitioned monthly,
retention configurable (default 7 years for financial records). Exposed read-only under
`audit.read`.

## Hardening checklist (implemented in Phase 1, verified each release)

- Rate limiting on `/connect/token`, PIN verification and lookup endpoints (`AddRateLimiter`).
- Account lockout, password policy, optional TOTP MFA for `Administrator`.
- CORS: exactly one origin (the BFF); SignalR restricted identically.
- Security headers: HSTS, CSP (no `unsafe-inline`, nonce-based), `X-Content-Type-Options`,
  `Referrer-Policy`, `Permissions-Policy`.
- Antiforgery on BFF mutating routes (defence in depth alongside `SameSite`).
- Dependency scanning (`dotnet list package --vulnerable`, `npm audit`) gating CI.
- No stack traces to clients; correlation id returned instead.
- Backups encrypted and restore-tested (doc 10).
