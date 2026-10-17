/* ===========================================================================================
   Turn a copy of a working Retail 25 database into one that is safe to hand out.

   Run this against a RESTORED COPY, never against a shop's database. The guard below stops
   it if you forget: it refuses to run against anything called "retail25", which is the name
   development and production both use.

   Two kinds of change happen here, and the difference matters.

     Rows that are DELETED are runtime or security state. A token, a data-protection key or a
     Hangfire job is not data anybody wants in a sample; it is live security material or the
     contents of a queue, and the right amount of it to publish is none.

     Rows that are REWRITTEN are business records worth keeping -- a customer, a sale, a staff
     member -- whose identifying fields are replaced. The shape of the data survives, so
     reports still show something, and no real person is in it.

   Everything a shop actually sells is left alone: products, prices, stock, tags, sales lines,
   ledger entries, tax configuration. That is the part worth sending.
   =========================================================================================== */

SET NOCOUNT ON;

DECLARE @db sysname = DB_NAME();
IF @db IN (N'retail25', N'master', N'msdb', N'model', N'tempdb')
BEGIN
    -- RAISERROR substitutes from variables, not from function calls, hence @db above.
    RAISERROR(N'Refusing to run: this must be executed against a restored copy, not %s.', 16, 1, @db);
    SET NOEXEC ON;
END
GO

/* -------------------------------------------------------------------------------------------
   1. Delete outright: live security state and runtime noise.
   ------------------------------------------------------------------------------------------- */

-- Issued tokens and the grants behind them. These are credentials in their own right: anyone
-- holding one can act as the subject it was issued to until it expires.
DELETE FROM dbo.OpenIddictTokens;
DELETE FROM dbo.OpenIddictAuthorizations;

-- The keys that encrypt the session cookie. Publishing these would let a reader forge one.
DELETE FROM dbo.data_protection_keys;

-- The audit log. Fifteen thousand rows of who did what, with payloads that quote the records
-- they changed -- so it carries copies of the very fields sanitised further down.
DELETE FROM dbo.audit_log_entries;

-- Carts in flight. Session state belonging to whoever was mid-sale when the copy was taken.
DELETE FROM dbo.cart_lines;
DELETE FROM dbo.carts;

-- Accounting sync history and the shop's uploaded logo.
DELETE FROM dbo.sync_logs;
DELETE FROM dbo.branding_assets;

-- Identity side tables: external logins, per-user claims and stored tokens.
IF OBJECT_ID(N'dbo.AspNetUserTokens') IS NOT NULL DELETE FROM dbo.AspNetUserTokens;
IF OBJECT_ID(N'dbo.AspNetUserLogins') IS NOT NULL DELETE FROM dbo.AspNetUserLogins;
IF OBJECT_ID(N'dbo.AspNetUserClaims') IS NOT NULL DELETE FROM dbo.AspNetUserClaims;
IF OBJECT_ID(N'dbo.AspNetRoleClaims') IS NOT NULL DELETE FROM dbo.AspNetRoleClaims;

-- Hangfire's queue, job arguments and server registrations. Job arguments carry entity ids
-- and occasionally the values a job was told to post.
IF OBJECT_ID(N'HangFire.JobParameter')     IS NOT NULL DELETE FROM HangFire.JobParameter;
IF OBJECT_ID(N'HangFire.State')            IS NOT NULL DELETE FROM HangFire.State;
IF OBJECT_ID(N'HangFire.JobQueue')         IS NOT NULL DELETE FROM HangFire.JobQueue;
IF OBJECT_ID(N'HangFire.Job')              IS NOT NULL DELETE FROM HangFire.Job;
IF OBJECT_ID(N'HangFire.List')             IS NOT NULL DELETE FROM HangFire.List;
IF OBJECT_ID(N'HangFire.Set')              IS NOT NULL DELETE FROM HangFire.[Set];
IF OBJECT_ID(N'HangFire.Hash')             IS NOT NULL DELETE FROM HangFire.Hash;
IF OBJECT_ID(N'HangFire.Counter')          IS NOT NULL DELETE FROM HangFire.Counter;
IF OBJECT_ID(N'HangFire.AggregatedCounter') IS NOT NULL DELETE FROM HangFire.AggregatedCounter;
IF OBJECT_ID(N'HangFire.Server')           IS NOT NULL DELETE FROM HangFire.Server;
GO

/* -------------------------------------------------------------------------------------------
   2. Rewrite: staff accounts.

   The password becomes a published placeholder rather than nothing, because an account with a
   null hash cannot be signed into and the seeder will not replace it either -- it only creates
   an administrator when no users exist at all. That combination leaves a database nobody can
   get into, which is how a sanitised copy ends up unused.

   The hash below is a genuine ASP.NET Core Identity v3 hash -- version byte, PRF, iteration
   count, salt and subkey -- of the password published in docs/sample-data/README.md. It is
   not a secret and is meant to be read.
   ------------------------------------------------------------------------------------------- */

UPDATE dbo.AspNetUsers
SET display_name          = CONCAT(N'Demo User ', id),
    user_name             = CONCAT(N'user', id, N'@example.invalid'),
    normalized_user_name  = UPPER(CONCAT(N'user', id, N'@example.invalid')),
    email                 = CONCAT(N'user', id, N'@example.invalid'),
    normalized_email      = UPPER(CONCAT(N'user', id, N'@example.invalid')),
    password_hash         = N'AQAAAAEAAYagAAAAEGmCqDCDB3A0BqX0EFmkxaGWO3T/syJ3xiBXE3fQnp2lp3hYCtSdyjgZwmHAlAG0hw==',
    security_stamp        = CONVERT(nvarchar(50), NEWID()),
    concurrency_stamp     = CONVERT(nvarchar(50), NEWID()),
    phone_number          = NULL,
    phone_number_confirmed = 0,
    lockout_end           = NULL,
    access_failed_count   = 0,
    last_signed_in_at     = NULL;

-- Till PINs are Argon2 hashes and are simply removed; PIN sign-in is then unavailable until an
-- administrator sets one, which is the correct state for a copy nobody owns.
-- Two things constrain this. staff_code is uniquely indexed, and a single UPDATE over every row
-- checks that index row by row -- so renumbering in place collides the moment one row is given a
-- code another row still holds. It is also nvarchar(8), while ids here run to five digits, so the
-- id cannot be part of the code at all. Hence a row number, and a parking pass on values nothing
-- can be using (no existing code is two characters long).
WITH numbered AS (SELECT id, ROW_NUMBER() OVER (ORDER BY id) AS rn FROM dbo.staff_profiles)
UPDATE s SET s.staff_code = CONCAT(N'T', n.rn)
FROM dbo.staff_profiles s JOIN numbered n ON n.id = s.id;

WITH numbered AS (SELECT id, ROW_NUMBER() OVER (ORDER BY id) AS rn FROM dbo.staff_profiles)
UPDATE s
SET s.first_name          = N'Demo',
    s.last_name           = CONCAT(N'Staff ', n.rn),
    s.staff_code          = CONCAT(N'ST', RIGHT(CONCAT(N'000', n.rn), 3)),
    s.pin_hash            = NULL,
    s.failed_pin_attempts = 0,
    s.pin_locked_until    = NULL
FROM dbo.staff_profiles s JOIN numbered n ON n.id = s.id;
GO

/* -------------------------------------------------------------------------------------------
   3. Rewrite: customers and shoppers.

   Identity goes; the commercial shape stays. Credit limits and balances are untouched, so the
   receivables screens still have something to show, and they no longer belong to anyone.
   ------------------------------------------------------------------------------------------- */

UPDATE dbo.customers
SET first_name                        = N'Demo',
    last_name                         = CONCAT(N'Customer ', customer_number),
    company                           = NULL,
    title                             = NULL,
    billing_address_line1             = N'1 Example Street',
    billing_address_line2             = NULL,
    billing_address_city              = N'Example City',
    billing_address_state_or_province = NULL,
    billing_address_postal_code       = N'00000',
    billing_address_country           = N'ZZ',
    ship_to_address_line1             = N'1 Example Street',
    ship_to_address_line2             = NULL,
    ship_to_address_city              = N'Example City',
    ship_to_address_state_or_province = NULL,
    ship_to_address_postal_code       = N'00000',
    ship_to_address_country           = N'ZZ',
    contact_phone                     = NULL,
    contact_extension                 = NULL,
    contact_mobile                    = NULL,
    contact_fax                       = NULL,
    contact_email                     = CONCAT(N'customer', customer_number, N'@example.invalid'),
    contact_website                   = NULL,
    birthday                          = NULL,
    notes                             = NULL;

IF OBJECT_ID(N'dbo.shoppers') IS NOT NULL
    UPDATE dbo.shoppers
    SET first_name       = N'Demo',
        last_name        = CONCAT(N'Shopper ', id),
        phone            = NULL,
        email            = CONCAT(N'shopper', id, N'@example.invalid'),
        normalized_email = UPPER(CONCAT(N'shopper', id, N'@example.invalid')),
        password_hash    = NULL,
        last_signed_in_at = NULL;
GO

/* -------------------------------------------------------------------------------------------
   4. Rewrite: the shop's own identity, and the machine client's secret.

   The client secret is removed rather than replaced. A terminal agent is configured per
   deployment anyway, and a working secret in a file that gets passed around is how an agent
   credential escapes.
   ------------------------------------------------------------------------------------------- */

UPDATE dbo.OpenIddictApplications
SET client_secret             = NULL,
    redirect_uris             = CASE WHEN redirect_uris             IS NULL THEN NULL ELSE N'["http://localhost:3000/signin-callback"]' END,
    post_logout_redirect_uris = CASE WHEN post_logout_redirect_uris IS NULL THEN NULL ELSE N'["http://localhost:3000/"]' END,
    concurrency_token         = CONVERT(nvarchar(50), NEWID());

UPDATE dbo.business_profiles
SET business_name             = N'Demonstration Store',
    address_line1             = N'1 Example Street',
    address_line2             = NULL,
    address_city              = N'Example City',
    address_state_or_province = NULL,
    address_postal_code       = N'00000',
    address_country           = N'ZZ',
    contact_phone             = NULL,
    contact_extension         = NULL,
    contact_mobile            = NULL,
    contact_fax               = NULL,
    contact_email             = N'shop@example.invalid',
    contact_website           = NULL,
    licence_number            = NULL,
    tax_registration_number   = NULL;

-- The location keeps its time zone and currency: those are configuration the system needs to
-- behave correctly, and neither identifies anybody.
UPDATE dbo.locations
SET name                      = N'Main Store',
    address_line1             = N'1 Example Street',
    address_line2             = NULL,
    address_city              = N'Example City',
    address_state_or_province = NULL,
    address_postal_code       = N'00000',
    address_country           = N'ZZ',
    contact_phone             = NULL,
    contact_extension         = NULL,
    contact_mobile            = NULL,
    contact_fax               = NULL,
    contact_email             = N'shop@example.invalid',
    contact_website           = NULL;
GO

SET NOEXEC OFF;
GO
