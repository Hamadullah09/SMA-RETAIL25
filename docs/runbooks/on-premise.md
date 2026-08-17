# Running Retail 25 in the shop

Putting the server on the shop's own network instead of renting it in a datacentre. Written to be
followed by somebody who did not build the system.

---

## 0. Why you would

Four problems on the hosted deployment have one cause: a till is a local appliance and the server is
a thousand miles away.

| | Hosted | In the shop |
|---|---|---|
| **RFID over the LAN** | Impossible. The reader lives at a private address (`192.168.x.x`) which is not routable from a datacentre. Not a firewall setting — there is no route. | Works. The API opens the reader directly. |
| **WebSockets** | The shared host will not upgrade the connection, so every live screen falls back to long-polling. | Work. |
| **Cold start** | ~17 seconds after the pool unloads, which it does when the shop is quiet. | None. The service stays running. |
| **Backup and restore** | The database is on another machine, so `BACKUP DATABASE` writes somewhere the application cannot reach. Only the portable export works, and it cannot be restored. | Native SQL Server backup **and restore** both work. This is the only way to get a real disaster-recovery story. |

That last row is the one to weigh most heavily. A shop that cannot restore does not have backups.

**What does not change:** you still need the terminal agent on each till that has a printer, a cash
drawer, a pole display, or a reader on a USB lead. Those are attached to a particular machine and
only something running on that machine can drive them. Hosting the API locally removes the agent's
job of *reaching the reader over the network*, not its job of driving the hardware in front of it.

---

## 1. The machine

One PC, kept on, wired rather than wireless, with a static address or a DHCP reservation. It runs
three things: SQL Server, the API, and the front end.

- **Windows 10/11 or Windows Server.** Nothing here needs Server.
- **8 GB RAM** is comfortable for a single shop; 4 GB works.
- **An SSD.** The database is small but a spinning disk makes every till feel slow.
- **Not the manager's laptop.** It has to be on when the shop opens.

Give it a name the tills can use — `pos.shop.local` below. A hosts-file entry on each till is enough
if you have no local DNS.

---

## 2. SQL Server

**SQL Server Express** is free and caps at 10 GB, which is years of a single shop's sales. Install
it with mixed-mode authentication, then create the database and a login for the application:

```sql
CREATE DATABASE retail25;
GO
CREATE LOGIN retail25_app WITH PASSWORD = 'choose-a-long-one';
GO
USE retail25;
CREATE USER retail25_app FOR LOGIN retail25_app;
ALTER ROLE db_owner ADD MEMBER retail25_app;
GO
```

`db_owner` because the application applies its own migrations at startup.

Enable TCP/IP in **SQL Server Configuration Manager** if the API will run as a different account.
For a single machine, `Server=.\SQLEXPRESS;Trusted_Connection=True` avoids the password entirely.

---

## 3. Keys

OpenIddict signs and encrypts every token with these. Outside Development the application refuses to
start without them, deliberately: the development fallback keeps its key in the launching user's
certificate store, and a key that regenerates on restart signs the whole shop out at intervals
nobody can account for.

Generate an encrypted pair. These commands are the ones to run — `openssl` ships with Git for
Windows at `C:\Program Files\Git\mingw64\bin\openssl.exe` if it is not already on your path:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -aes-256-cbc -pass pass:YOUR-PASSWORD -out retail25-signing.pem
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -aes-256-cbc -pass pass:YOUR-PASSWORD -out retail25-encryption.pem
```

Put both in a `certs` folder beside the API and **back them up somewhere else**. Losing them signs
everybody out once; that is all, but it is avoidable.

`.pem` rather than `.pfx` for the same reason as the hosted deployment: a PEM is parsed straight into
an in-memory key with no certificate store, no key container and no temp file.

---

## 4. HTTPS on a network with no public certificate authority

The API refuses plain HTTP outside Development, and it should — an authorization code crossing even
a shop's own wiring in the clear is still the whole attack.

There is no public CA that will issue for `pos.shop.local`, so issue your own:

```powershell
$cert = New-SelfSignedCertificate `
  -DnsName 'pos.shop.local', 'localhost' `
  -CertStoreLocation 'Cert:\LocalMachine\My' `
  -NotAfter (Get-Date).AddYears(5) `
  -FriendlyName 'Retail 25 POS'

Export-Certificate -Cert $cert -FilePath C:\retail25\pos-public.cer
```

Then **install `pos-public.cer` into Trusted Root on every till**, or the browser warns on every
load and cashiers learn to click through warnings, which is worse than no certificate at all.

Five years so that renewing it is a diary entry rather than a surprise on a trading day.

---

## 5. The application

Publish both halves on your machine and copy them over:

```bash
dotnet publish backend/src/Retail25.Api -c Release -o publish/api
cd frontend && npm ci && npm run build
```

Run the API as a **Windows service** so it starts with the machine and survives a logout. It already
supports this — the terminal agent uses the same hosting — so `sc.exe create` is enough:

```powershell
sc.exe create Retail25Api binPath= "C:\retail25\api\Retail25.Api.exe" start= auto
sc.exe description Retail25Api "Retail 25 POS API"
sc.exe start Retail25Api
```

The front end is a Node application; run it the same way with a service wrapper, or host both behind
IIS if you would rather manage them there.

---

## 6. Configuration

Environment variables, not edits to a committed file:

| Setting | Value | Why |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Development disables security |
| `ConnectionStrings__DefaultConnection` | `Server=.\SQLEXPRESS;Database=retail25;Trusted_Connection=True;Encrypt=False` | Local, so no password to leak |
| `Auth__WebOrigin` | `https://pos.shop.local` | Exactly as the browser sees it |
| `AllowedOrigins__0` | `https://pos.shop.local` | Same |
| `OpenIddict__SigningCertificatePath` | `certs/retail25-signing.pem` | §3 |
| `OpenIddict__EncryptionCertificatePath` | `certs/retail25-encryption.pem` | §3 |
| `OpenIddict__CertificatePassword` | what you chose in §3 | |
| `Cache__Provider` | `SqlServer` | One instance needs no Redis |
| `Backup__Mode` | `Native` | **The database is local now, so real SQL backups work** |
| `Backup__Directory` | `D:\retail25-backups` | See below — the *SQL Server service* writes here, not the API |
| `Rfid__ServerReaders__Enabled` | `true` | The point of the exercise |

The application refuses to start if `Auth__WebOrigin` is still the shipped placeholder, so a
half-configured deployment fails loudly on the first run rather than quietly weeks later.

### The backup folder needs one permission

`BACKUP DATABASE` is executed *by SQL Server*, not by the API, so the folder must be writable by the
SQL Server service account rather than by the application's:

```powershell
icacls D:\retail25-backups /grant "NT SERVICE\MSSQL$SQLEXPRESS:(OI)(CI)M"
```

Miss this and the backup fails with an access error naming a path that looks perfectly writable,
because you are looking at it as yourself.

**On Express, backups are not compressed.** `WITH COMPRESSION` is a Standard and Enterprise feature;
on Express the statement fails outright and writes nothing. The application's own backup command
does not use it, so this only matters if you are running `BACKUP` by hand from `restore.md` — that
runbook now says so too.

Verified on Express 2022: a 31 MB database backs up in a third of a second and `RESTORE VERIFYONLY`
reports a valid set.

---

## 7. The reader

Set the reader profile's host to the reader's address on the shop LAN — **Administration → Settings
→ Hardware** — and give the reader a DHCP reservation so it does not move.

The API will find it anyway if it does move: the address sweep looks across the local /24. But an
address that changes under you is a thing to fix rather than to work around.

`Rfid__ServerReaders__Enabled=true` is what makes the API open the connection itself. One instance
only — a UHF bridge accepts a single client, so two instances would fight over it.

---

## 8. Check it worked

In order, because each answers a different question:

1. **`https://pos.shop.local/backend/health/ready`** → `Healthy`. The API started and reached SQL.
2. **`/backend/.well-known/jwks`** → one RSA key. The signing key actually loaded, as opposed to
   the process merely starting.
3. **Sign in from a till**, not from the server. That tests DNS, the certificate and the origin
   together.
4. **Ring a sale** for a known amount and check the total. A 30.00 item with 5% and 7% must come to
   **33.60**; if it does, configuration → pricing engine → cart is intact.
5. **Watch the browser console on the till.** No WebSocket fallback warning. That is the thing you
   moved for.
6. **Present a tag.** The reader panel goes from "Not connected" to reading.
7. **Take a backup, then restore it onto a spare machine.** Not the live one. A backup nobody has
   restored is a hope, and this is the first deployment where you can actually find out.

---

## 9. What you take on

Honestly, because it is a real trade:

- **The machine is yours.** Nobody else patches it, replaces its disk, or notices it is off.
- **Backups leave the building or they do not count.** A backup on the same disk as the database
  survives nothing that matters. Copy them off nightly.
- **Remote access, if you want it,** is a VPN or nothing. Do not put the till's port on the
  internet.
- **The UPS is not optional.** A POS that loses power mid-sale is exactly the case the ledgers were
  designed for, but a database on a disk that lost power mid-write is a restore.

None of that is worse than the hosted arrangement — it is the same work, done by you instead of
nobody.
