# Sanitised sample database

`retail25-sanitised.sql` is a working Retail 25 database — schema and rows in one file — with
every person taken out of it. 1.3 MB, 116 tables, 2,401 rows.

Use it when you want to see the system with something in it: a catalogue to sell from, stock to
move, tags to read, sales to reprint. It is not a substitute for
[`database-from-scratch.md`](../runbooks/database-from-scratch.md), which is how you build a
database for real.

## Load it

Into an **empty** database. It creates everything it needs.

```bash
sqlcmd -S .\SQLEXPRESS -d retail25_sample -i docs/sample-data/retail25-sanitised.sql
```

Then point the API at it and leave `Database:AutoMigrate` **off** — the schema is already there
and the migration history came with it, so EF will see a database at the current migration and
leave it alone.

### Signing in

Every account in the file shares one password, which is published here because it protects
nothing:

```
Sanitised#2026
```

The usernames are `user1@example.invalid`, `user2@example.invalid` and so on. Which of them is the
administrator is visible in `AspNetUserRoles`.

Till PINs are gone — the Argon2 hashes were removed rather than replaced, so PIN sign-in is
unavailable until an administrator sets one. The terminal agent's client secret is gone for the
same reason, so a till will not connect until you configure one.

## What was removed, and what was kept

Removed outright, because it is live security material or runtime noise rather than data anybody
wants:

| | Why |
|---|---|
| Issued tokens and OAuth grants | Credentials in their own right — holding one lets you act as its subject |
| Data-protection keys | They encrypt the session cookie; publishing them lets a reader forge one |
| Audit log (15,356 rows) | Quotes the records it describes, so it carries copies of the fields sanitised elsewhere |
| Carts in flight | Somebody's half-finished sale |
| Hangfire queue, job arguments, servers | Queue contents and machine names |
| The shop's uploaded logo | Identifies the business |

Rewritten, so the shape survives and the person does not: staff and customer names, usernames,
e-mail addresses, phone numbers, postal addresses, birthdays, free-text notes, the business
name, licence and tax-registration numbers, and the location name. Every password hash is
replaced by one published placeholder. Credit limits and outstanding balances are **kept** — they
make the receivables screens show something, and they no longer belong to anyone.

Kept exactly as it was, because this is the part worth having: products and their prices, stock
levels, the stock ledger, RFID tags and serialised units, sales and their tax snapshots, tender
types, tax configuration, pricing rules, permissions and roles, number sequences, and the reader,
printer, scale and pole-display profiles.

Roughly what that comes to: 170 products, 1,354 tagged units, 143 stock levels, 27 sale lines,
34 ledger entries, 61 permissions, 5 roles, 4 users, 1 customer.

## How it was made, and how to make another

1. `BACKUP`/`RESTORE` the source database under a different name. The original is never written to.
2. Run [`../runbooks/sanitise-database.sql`](../runbooks/sanitise-database.sql) against the copy.
   It refuses to run against a database called `retail25`, so a mistyped `-d` stops rather than
   destroys.
3. Script the copy's schema and rows to one file.

The script is committed so you can read exactly what was scrubbed rather than take this page's
word for it.

## What was checked before publishing

- The file **restores into an empty database with no errors**, and the restored database has the
  same 2,401 rows across the same tables as the sanitised copy it came from.
- It contains **no e-mail domain other than `example.invalid`**, and exactly **one** distinct
  password hash.
- It contains none of the real identifiers the source database held.

## Two things it will not survive

**Order.** Tables are created without their foreign keys, the rows go in, and the 19 foreign keys
are added at the end `WITH CHECK` — which validates everything just loaded. Scripted the obvious
way, `AspNetRoleClaims` is created before `AspNetRoles` and the file fails on line 18.

**Identity columns.** The original key values are kept so the foreign keys still line up, which
means the inserts need `SET IDENTITY_INSERT` around them. Strip those and the keys are silently
renumbered and the relationships quietly break.
