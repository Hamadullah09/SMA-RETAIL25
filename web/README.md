# SMA RETAIL web back office

The browser side of SMA RETAIL, for administrators and super administrators: the dashboard across stores, the tills
and their packages, sales, stock, clients and suppliers, gift cards, reports, staff, users and roles, stores, setup and
the audit log. It looks and works like the desktop app (same colours, type, buttons with their keys, Ctrl+K) and talks
only to `RetailPlus.Server`, through the endpoints in [`docs/API.md`](../docs/API.md).

Next.js 14 (App Router) with React 18, TypeScript, Tailwind CSS 3, TanStack Query, lucide-react and Recharts. It is a
**static export**: `npm run build` writes plain files to `web/out`, and the server serves them next to the API. There is
no Node server in production.

## Run it in development

You need Node 20 or later and a running server. From the repository root:

```
dotnet run --project src/RetailPlus.Server --urls http://localhost:5080
```

In Development the server lets http://localhost:3000 call it with credentials (`Cors:Origins`). Then:

```
cd web
npm install
copy .env.example .env.local      (or: cp .env.example .env.local)
npm run dev
```

Open http://localhost:3000 and sign in with a user whose role has *Sign in to the web back office* (`web.signin`), for
example the Administrator or Super administrator role. `NEXT_PUBLIC_API_BASE` in `.env.local` says where the API is;
leave it out when the site and the API share an origin, as they do in production.

## Build

```
npm run build
```

The site goes to `web/out` (one `index.html` per page, `404.html`, `_next/static/…`). RetailPlus.Server serves that
folder: `Server:WebRoot`, or by default the `web/out` it finds walking up from where it runs. Build the site before
publishing the server, or copy `web/out` next to it and point `Server:WebRoot` there. To look at the build without the
server, `npm run serve` serves `out/` on http://127.0.0.1:4173 the same way (with no API behind it).

Detail pages take their record in the address (`/sales/view/?id=…`, `/items/edit/?id=…`, `/reports/view/?key=…`),
because a static export has no dynamic routes.

## Check it

| Command | What it does |
|---|---|
| `npm run typecheck` | TypeScript, strict |
| `npm run lint` | ESLint (`next/core-web-vitals`, and no off-scale text sizes) |
| `npm test` | Unit tests (Vitest): the API client (error messages, the 401 redirect, the CSRF header), formatting, the permission-to-navigation filter, the date presets, report totals, form checks, keys, and a WCAG AA contrast check of every colour pair used for text |
| `npm run e2e` | End-to-end tests (Playwright) against the built site: run `npm run build` first. The API is answered in the browser from fixtures that follow `docs/API.md` (`e2e/fixtures.ts`, `e2e/mock-api.ts`), so no server is needed. Desktop (1366×768) runs every spec; phone (390×844) runs the layout spec |
| `npm run screens` | Screenshots of every page at 1366×768, 1024×768 and 390×844, into `SCREENS_DIR` (default `test-results/screens`) |

The first time, install the browser: `npx playwright install chromium`.

## How it is put together

| Where | What |
|---|---|
| `src/lib/api.ts` | The one API client: `credentials: 'include'` on every call, `X-Requested-With: retailplus-web` on every change, an `ApiError` with the server's `error` message, a 401 back to `/sign-in?next=…`, "Your role cannot open this page." for a 403 |
| `src/lib/types.ts` | A TypeScript type for every shape in `docs/API.md` |
| `src/lib/nav.ts` | The pages, the permission each needs, the Ctrl+1 … Ctrl+0 keys and the Ctrl+K commands |
| `src/lib/tokens.ts`, `src/app/globals.css`, `tailwind.config.ts` | The desktop's colour tokens (from `src/RetailPlus.App/App.xaml`), radius 14 and 10, Century Gothic headings and 17 px Segoe UI text |
| `src/components/shell` | The frame: navigation panel, header pills, the Go to palette, the phone drawer |
| `src/components/ui` | Buttons with key caps, fields, dialogs, tabs, grids, states |
| `src/app/(app)/…` | One folder per page |

### Rules the code keeps

- The session token lives only in the server's httpOnly `rp_session` cookie. Nothing in the app stores it; the only
  thing kept in the browser is the chosen store (`localStorage`).
- Every list sends the store chosen in the header as `storeId` (none for "All stores").
- Money uses the currency symbol that comes with the store list (`GET /api/stores`) and `Intl` for the rest of the
  formatting. Business dates are
  the browser's local days; weeks start on Monday.
- Items are kept per location on the server, so Inventory has a line per store that stocks a code, and a new item is
  added to the store and location chosen in its editor.
- Transfers and stock counts are listed a page at a time, searched by number or location.
- Shared lists (clients, suppliers, departments, categories, roles, commission rules) change only for users whose
  `/api/auth/me` says `canChangeShared` (super administrators, or anyone while there is one store); for the others the
  add, edit and delete buttons are hidden or disabled. Departments and categories are kept from Inventory.
- Staff and user filters (Sales Journal, Staff, Audit log) use `GET /api/staff`.
- The server answers `/tills` (no slash) with the export's `404.html`; that page moves on to `/tills/`.
- Keys: Ctrl+K goes anywhere, Ctrl+1 … Ctrl+0 (or Alt+1 … Alt+0) open the first ten pages, `/` finds in a list, and
  every button shows the key that presses it. Chrome keeps Ctrl+N for a new window, so "new" buttons show Alt+N (Ctrl+N
  still works where the browser lets it through).
- At 1280 px and wider it looks like the desktop; from 768 px the navigation shows icons only; below that it is a
  drawer, and tables scroll inside their card.
