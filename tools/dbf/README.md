# DBF tools

Read-only Python 3 scripts for inspecting Retail Plus data folders. They need no packages beyond the standard library.

| Script | What it does | Example |
|---|---|---|
| `dbf_schema.py` | Prints every table structure in a folder (handles C fields longer than 255) | `python dbf_schema.py "C:\Retail Plus 52\POS"` |
| `dbf_dump.py` | Prints records of one table, resolving FPT memos; optional field list and record limit | `python dbf_dump.py POSINV.DBF PROD,PLU,MATRIX 5` |
| `cdx_tags.py` | Lists the tags of CDX indexes with their key and FOR expressions | `python cdx_tags.py "C:\Retail Plus 52\*.CDX"` |
| `gen_schema_md.py` | Regenerates the table appendix of `docs/DATA_FORMATS.md` | `python gen_schema_md.py "C:\Retail Plus 52" out.md cdx_tags.py` |
| `cdx_check.py` | Checks a table's production CDX against its records: rebuilds every key from the tag expression (VO encodings, including `DESCEND(date)`), then reports missing, extra, duplicate, wrong or out-of-order keys | `python cdx_check.py POSINV.DBF` |
| `dbf_diff.py` | Byte-level comparison of two copies of a table: header, field descriptors, per-field changes, memo text vs memo block moves | `python dbf_diff.py before\POSINV.DBF after\POSINV.DBF` |
| `dbfread.py` | Shared reader used by the two tools above | |
| `dbf_to_json.py` | **Test oracle** for the C# reader: writes a table and its memos as canonical JSON. The rules (text, numbers, dates, logicals, memos, invalid values) are in the script's header | `python dbf_to_json.py POSINV.DBF out.json` |
| `make_fixtures.py` | Regenerates the synthetic edge-case tables and their golden JSON in `tests/RetailPlus.Migration.Tests/Fixtures/dbf` | `python make_fixtures.py tests/RetailPlus.Migration.Tests/Fixtures/dbf` |

If you change a rule in `dbf_to_json.py`, change the C# reader (`src/RetailPlus.Migration/Dbf`) to match and regenerate the fixtures. The tests compare the two on every fixture, and on real data when a data folder is available.

`dbf_dump.py` prints record contents. Don't paste its output from real store data (client names, emails, phone numbers) into tickets or chats.
