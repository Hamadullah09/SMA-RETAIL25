# Cutover runbook

Moving a shop from Retail Plus 2.5 to Retail 25. Written to be followed by someone who did not build
the system, on a day when the shop is closed and the clock is running.

**Before you start, read the two honest caveats in §0.** They change what this document can promise.

---

## 0. What this runbook can and cannot promise

**The importers are tested; your data is not.** Every step below has been exercised end to end against
synthetic files built to the field orders the legacy guide documents (p.28, p.48, p.61, p.17, p.22).
That proves the code reads the format correctly. It does **not** prove it reads *your* files
correctly — a shop that has been running for twenty years accumulates conventions nobody wrote down.
The first thing you do with a real extract is §3, and if the analysis report looks wrong, stop.

**Nothing here is a substitute for the parallel-run window (§8).** The import can reconcile perfectly
and still have brought across something subtly wrong. Two weeks of both systems running is what
catches that, and it is the cheapest insurance in this document.

---

## 1. Roles

| Role | Who | What they own |
|---|---|---|
| Cutover lead | someone from the shop | The go/no-go call. Holds the old system's control-total printouts. |
| Technical | someone who can run `psql` | Backups, restores, the rollback. |
| Floor check | a senior member of staff | Spot-checks that items, prices and clients look right. |

Do not start without all three available for the whole window.

---

## 2. Before the day

- [ ] Retail 25 is deployed, reachable, and an administrator can sign in.
- [ ] Locations exist, with the legacy three-character codes in `LegacyCode`.
- [ ] Tax configuration is set up and correct. **The importer does not bring taxes across** — a wrong
      tax rate is worse than no data, so this one is deliberately manual.
- [ ] Tender types match what the shop actually takes.
- [ ] At least one member of staff exists with a PIN, so a till can be opened after cutover.
- [ ] A rehearsal has been done on a copy of the database, following this document to §7.

**Print these from the old system on the morning of the cutover and keep the paper:**

- Inventory valuation report — item count and total value at cost.
- Client list — count, and total balance outstanding.
- Supplier list — count.
- Year-to-date sales total.

These are the control totals. Everything in §6 compares against them, and there is no way to derive
them once the old system is switched off.

---

## 3. Freeze

Nothing below is reversible cheaply once §7 has run, so the freeze is what makes the rest safe.

1. Stop trading on the old system. **No sales, no receiving, no client edits from this point.**
2. Note the exact time. Write it down.
3. Take the final backup of the legacy data — the whole directory, not just the `.DBF` files. The
   `.FPT` memo files sit beside them and are easy to leave behind.

```bash
# Adjust the paths. The point is the whole directory, and a copy nobody can accidentally write to.
tar -czf retailplus-final-$(date +%Y%m%d-%H%M).tar.gz /path/to/retailplus/data
```

4. Copy that archive somewhere off the machine. A cutover that loses its own rollback source is the
   worst outcome in this document.

---

## 4. Back up Retail 25

Even on a fresh install. The rollback in §9 restores this, and taking it costs thirty seconds.

```bash
pg_dump --format=custom --file=retail25-pre-cutover.dump "$RETAIL25_CONNECTION"
```

Verify it is not empty:

```bash
pg_restore --list retail25-pre-cutover.dump | head
```

---

## 5. Read the files in

In Retail 25: **Administration → Bring data across.**

Do them in this order. It matters: an invoice needs its client to exist, and an item needs somewhere
to hang its department.

1. **Suppliers** (`supplier file`, or the 15-column CSV from p.61)
2. **Clients** (`CLIENT.DBF` + `CLIENT.FPT`, or the 14-column CSV from p.48)
3. **Inventory** (`XXXINV.DBF` + `.FPT`, or the 11-column CSV from p.28) — once per location
4. Anything else you have

For each file:

- [ ] Choose the file type, pick the file, upload.
- [ ] **Read the analysis report.** Check: does the row count match what the old system said? Does
      the column count match the documented layout? Are the sample values in the right columns?
- [ ] If the analysis says the file has memo columns and you did not upload the `.FPT`, stop and get
      it. Memo columns import empty, silently, and a client's purchase-history notes are exactly the
      sort of thing nobody notices is missing until months later.

**If the sample values are in the wrong columns, stop.** That means the field order differs from the
documented one, and everything downstream will be wrong in a way that reconciles perfectly.

---

## 6. Check, then rehearse

For each file, in the same order:

1. **Check it.** Read every blocking problem. They are addressed to a row and a column.
2. Fix blocking problems **in the source file**, then re-upload. Do not work around them.
3. Type the control totals from §2 into "What the old system said".
4. **Dry run.** This transforms every row exactly as the import will and writes nothing.
5. **Read the reconciliation.** Every measure with an old-system figure beside it must say `yes`.

If a measure says `NO`, the difference tells you what to look for:

| Symptom | Usual cause |
|---|---|
| Item count short by a few | Rows the old system had deleted but never packed. Expected — check the count matches the "deleted" figure on the analysis. |
| Item count short by many | Blocking errors are excluding rows. Go back to step 1. |
| Inventory value out by a round factor | Cost and price columns swapped, or a decimal-place difference in the export. |
| Inventory value out by a little | Items with a blank cost. The analysis names them. |
| Client count matches, balances do not | Invoices have not been imported yet. That is a separate file. |

**Do not proceed to §7 with a `NO` you cannot explain.**

---

## 7. Import

Only when §6 is clean for that file.

- [ ] Press Import. Confirm the count.
- [ ] Read the report it produces. It is the same shape as the dry run's — the figures should match.

The import writes:

- Suppliers, clients and their accounts, departments, categories, items.
- Opening stock as **stock-ledger entries** with the reason `Legacy opening balance`, never as raw
  on-hand figures. The ledger is authoritative from row one, so a stock valuation after cutover
  replays from the same entries as one taken a year later.
- An external mapping per row, keyed by the legacy identifier. That is what makes a second attempt
  update rather than duplicate.

Repeat §5–§7 for each file.

---

## 8. Verify on the floor

Off the screen and onto the shop floor. Fifteen minutes here is worth more than any report.

- [ ] Pick ten items at random from a shelf. Scan or type each into the new system. Price right?
      Description right? Department right? On-hand plausible?
- [ ] Pick the three items with the highest on-hand figures. Do they look right, or has a decimal
      moved?
- [ ] Pick five clients. Are their names, addresses and credit limits right?
- [ ] Ring a real sale on a real till, take real cash, and close the drawer. Does it reconcile?
- [ ] Run the stock valuation report. Does it match §6?

**Parallel run.** For the next two weeks, keep the old system's final backup available and reconcile
the daily takings between what the new system reports and what the shop counted. This is what catches
the wrong thing that reconciled perfectly.

---

## 9. Rollback

If §6 or §8 goes wrong in a way you cannot explain, roll back. It is cheap now and expensive later.

```bash
# 1. Stop the API so nothing writes during the restore.
#    (systemctl stop retail25-api, or however it is run.)

# 2. Restore the pre-cutover dump. --clean drops what is there first.
pg_restore --clean --if-exists --dbname "$RETAIL25_CONNECTION" retail25-pre-cutover.dump

# 3. Start the API and confirm the database is back to where it was.
```

Then: unfreeze the old system and resume trading on it. The legacy data was never touched — every
step above reads from a copy — so the shop can carry on while the problem is worked out.

**A batch that has been imported cannot be un-imported from the UI.** That is deliberate: a partial
undo across a catalogue, a stock ledger and a set of accounts is far more dangerous than a restore.
The dump from §4 is the undo.

---

## 10. After

- [ ] The old system is read-only, or off.
- [ ] The final legacy archive from §3 is stored somewhere that survives this machine.
- [ ] The pre-cutover dump from §4 is kept for at least the parallel-run window.
- [ ] The reconciliation reports are saved. They are the record of what came across, and the first
      thing anyone will want in six months.
- [ ] Staff know: the time clock is in the header, the price checker is on the till, and Ctrl+K finds
      any screen.

---

## Appendix: what does not come across

Stated plainly so nobody discovers it by accident.

| Not imported | Why | What to do |
|---|---|---|
| Tax configuration | A wrong rate is worse than no data | Set up by hand before cutover (§2) |
| Historical sales | Bounded and optional by design; not built yet | The old system's reports remain the record |
| Purchase orders | The legacy system did not convert these between its own versions either (guide p.103) | Re-raise open POs by hand |
| Exit totals / POS history | As above | Keep the printouts |
| Back orders | As above | Re-enter by hand |
| Staff PINs | Never migrate a credential | Set new PINs |
| Register `.ASC` sales and stock-count files | Read and checked, but the importers are not built yet — the screen says so rather than reporting a successful import of nothing | Use the stock-count screen directly |
