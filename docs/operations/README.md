# Operations

One-off scripts that have been run against a real database, kept because the database cannot
explain itself afterwards.

A migration in `Infrastructure/Persistence/Migrations` changes the schema and EF records that it ran.
Nothing records a script that rewrote the *data* — and "why is every price a suspiciously round
number?" is a question somebody will ask eventually, possibly years later, possibly of an accountant.
These files are the answer.

Named for the day they were run. They are history, not tooling: do not re-run one to "reapply" it.
Most are not idempotent, and running the currency conversion twice would multiply every price by the
rate again.

| Script | Ran against | What it did |
|---|---|---|
| `2026-08-10-convert-cad-to-pkr.sql` | `retail25` on 2026-08-10 | Multiplied every monetary amount by 205, converting the shop from Canadian dollars to Pakistani rupees. |

## The currency conversion, in more detail

Catalogue prices were rounded to the whole rupee; ledger and historical amounts were multiplied
exactly. That split matters. Prices are inputs a shopkeeper sets and a shelf label prints, so a round
number is what they want. Ledger figures have to keep agreeing with each other — a sale's total is
the sum of its lines plus tax, a tender matches what was owed — and rounding each column on its own
would break those sums independently and leave books that no longer balance.

Verified afterwards: every sale with lines reconciled to zero difference.

Deliberately not converted, because they are not money: tax rates, discount and margin percentages,
quantities, weights, hours and stock levels. Also left alone:

- `currencies.minimum_tender`, already set to 1 rupee by hand.
- `commission_rules.value`, which may hold either a fixed amount or a percentage and cannot be told
  apart from the column alone. **A shop using fixed-amount commission rules still has old-currency
  figures there.**

`loyalty_policies.points_per_dollar` was *divided* by the rate rather than multiplied. Points are
earned per unit of currency, so leaving it alone would have handed out 205 times the rewards.

A backup was taken first, to `C:\ProgramData\Retail25\Backups\retail25_before_pkr_conversion.bak`.
