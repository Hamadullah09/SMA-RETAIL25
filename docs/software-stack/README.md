# Software Stack Documentation

`software_stack_documentation.pdf` — a 46-page technology inventory, architecture and
deployment reference for Retail25, generated from repository evidence on branch `main`
at commit `e7f91ad`.

`software_stack_documentation.tex` is the complete source. The PDF is built from it and
nothing else: there are no external image files, because every diagram is drawn in TikZ
inside the source.

## Rebuilding the PDF

```bash
pdflatex -interaction=nonstopmode software_stack_documentation.tex
```

Run it **four times**. The document has a table of contents, a list of figures, a list of
tables, and `longtable`s whose column widths settle over successive passes; fewer runs
leave stale page numbers. `latexmk -pdf software_stack_documentation.tex` does the same
thing and decides the pass count for you.

### What the build needs

A LaTeX distribution with the standard packages — `geometry`, `booktabs`, `longtable`,
`tabularx`, `fancyhdr`, `enumitem`, `hyperref`, `bookmark`, `pdflscape` and `tikz` with
the `shapes.geometric`, `arrows.meta`, `positioning`, `fit`, `backgrounds` and `calc`
libraries. No shell-escape, no bibliography tool, no external converters.

Built and verified here with MiKTeX 25.12 / MiKTeX-pdfTeX 4.23 on Windows. On a fresh
MiKTeX install, run `initexmf --update-fndb` first and build the format once with
`miktex --enable-installer formats build pdflatex --engine pdftex`; otherwise the first
`pdflatex` run fails looking for `.sty` files that are in fact already installed.

## A note on the source

Two things in the preamble are load-bearing and should not be simplified away.

**`\pathx`, `\code` and `\pkg` are two-stage macros.** Each opens a group, makes `/`,
`.`, `-` and `@` active so they become break opportunities, and only then reads its
argument. The catcodes have to change *before* the argument is tokenised, which is why
these cannot be written as ordinary one-argument commands. Without them a path like
`app/api/proxy/[...path]/route.ts` is a single unbreakable word and runs off the page —
76 boxes overflowed, one by 7cm, before this existed.

**The tables are `longtable`, not `tabularx`.** `tabularx` collects its whole body as a
macro argument so it can re-typeset it while solving for the `X` column, which fixes
every token in the table before any macro inside a cell runs — so the breaking above
never fires inside one. `longtable` reads rows incrementally and the same cells break
correctly. The `X` columns were replaced with explicit widths computed against the
16.2cm text block.

The document currently compiles with **no errors, no overfull boxes and no undefined
references**.

## Scope and honesty rules this document was written under

- Every technology, version and component was read from a file in the repository. The
  evidence column of each table names that file.
- .NET package versions are exact, from `backend/Directory.Packages.props` (Central
  Package Management, so declared and effective versions are the same). Front-end
  versions are the *locked* versions from `frontend/package-lock.json`, with the
  declared ranges shown alongside.
- Anything that could not be established is labelled **Not Found**, **Not Configured**
  or **Not Verified** rather than guessed. Capacity figures are the main example: no
  manifest, autoscaling policy or load test in the repository states one, so none is
  given.
- Inferences are labelled **Derived** and recommendations **Recommended**, so neither
  can be mistaken for an existing project requirement.
- No connection string, password, key or token appears anywhere in the document.

## Known divergences between the code and the project's own documentation

Chapter 17 records these in full with evidence. The three that matter most, because an
engineer following the existing docs would get them wrong:

| Topic | Documentation says | Code shows |
|---|---|---|
| Database | PostgreSQL (`CLAUDE.md`, `docs/architecture/README.md`) | SQL Server (`UseSqlServer`) |
| Staff sign-in | Authorization Code + PKCE | A custom JWT endpoint, `POST /auth/token` |
| Parity benchmark | `docs/BENCHMARK.md` is authoritative | Neither it nor `capabilities.json` exists on `main` |

## Known defects in this document

Two of the six TikZ figures have layout faults that are cosmetic but visible, and are
not yet fixed:

- **Figure 3.1 (System architecture)** — several edge labels are drawn over node boxes,
  and the browser-to-hubs curve passes through the BFF box. The content is correct; the
  routing is not.
- **Figure 4.1 (Application runtime flow)** — in the sign-in branch, the arrow from
  *Credentials accepted? → yes* crosses the `POST /api/auth/sign-in` box.

Both need the affected `\draw` paths rerouted onto an explicit grid rather than the
current freehand control points. The other four figures render cleanly.

## Files

```
docs/software-stack/
  software_stack_documentation.tex   complete source
  software_stack_documentation.pdf   46 pages, built from the above
  diagrams/                          empty: all figures are TikZ in the source
  README.md                          this file
```

`diagrams/` is kept because the brief asked for it, but nothing belongs in it while the
figures are drawn inline — a separate image file would be a second copy of a diagram
that could then disagree with the source.
