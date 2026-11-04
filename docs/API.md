# Server API for the web back office

The contract between `src/RetailPlus.Server` (phase 3) and the web app in `web/` (phase 4). Both are built against this
file at the same time, so names and shapes here are binding: the server may add fields and endpoints, never rename or
drop one listed here; the web app uses only what is listed here (plus what already existed: health, sign-in, stores,
stations, enrollment, sync batches, package upload).

## Conventions

- Everything is under `/api`, JSON with camelCase names (ASP.NET Core defaults). Enums are strings.
- **Sign-in.** `POST /api/auth/sign-in` returns the token as before and also sets an httpOnly, SameSite=Strict cookie
  `rp_session` (Secure outside Development) holding it. The server accepts the token from the `Authorization: Bearer`
  header or, when there is none, from that cookie. The web app relies on the cookie only and never stores the token.
  `POST /api/auth/sign-out` clears the cookie (204). Requests that change data and are authenticated by the cookie must
  carry the header `X-Requested-With: retailplus-web` (CSRF guard); the server refuses them with 403 otherwise.
- **Errors:** `{ "error": "message for the user" }` with 400 (validation or business rule, from `BusinessRuleException`),
  401 (not signed in), 403 (no permission, or a store the user may not see), 404, 409 (someone else changed the record;
  message says so).
- **Store scope:** users with `stores.all` see every store; other users only their home store (`UserScope`). Every list
  takes an optional `storeId`; without it, a list covers all stores the user can see. A `storeId` the user cannot see is
  403.
- **Dates:** business dates are `yyyy-MM-dd` (`from`, `to` inclusive; default today, in the store's time zone). Fields
  ending in `Utc` are UTC ISO-8601 with `Z`; other date-times are the store's local time without an offset, as tills
  record them.
- **Paging:** list endpoints that say *paged* take `page` (1-based, default 1) and `pageSize` (default 50, max 500) and
  return `{ "items": [...], "total": 1234, "page": 1, "pageSize": 50 }`.
- **Money** is a JSON number with 2 decimals; quantities up to 3 decimals.
- **Concurrency:** editable records carry `version` (string). PUT sends it back; a mismatch is 409.
- **Permissions** (keys from `Core/Rules/Permissions.cs`) are listed per endpoint. Every endpoint also needs
  `web.signin`.

## Already there (phase 2)

| Method and path | Permission | Notes |
|---|---|---|
| `GET /api/health` | none | `{ status, database, version }` |
| `POST /api/auth/sign-in` | none | body `{ userName, password }` → `{ token, expiresAtUtc, user: UserInfo }` + cookie |
| `GET /api/auth/me` | web.signin | `UserInfo { id, userName, fullName, role, permissions[], storeId, storeName, mustChangePassword, canChangeShared }` |
| `POST /api/auth/change-password` | web.signin | `{ currentPassword, newPassword }` → 204 |
| `GET /api/stores` | web.signin | `[StoreInfo { id, code, name, timeZoneId, currencySymbol }]` |
| `POST /api/stores`, `PUT /api/stores/{id}` | stores.manage | `{ code, name, timeZoneId }` |
| `GET /api/stores/{id}/stations` | web.signin | `[StationInfo]` (see the server's `Endpoints.cs`) |
| `POST /api/stores/{id}/stations` | stores.manage | `{ number?, name? }` → `EnrollmentCodeInfo { station, code, expiresAtUtc }` |
| `POST /api/stations/{id}/enrollment-code` | stores.manage | new code |
| `POST /api/stations/{id}/retire` | stores.manage | |
| `GET /api/sync/batches?storeId&station&status&take` | web.signin | `[BatchInfo]` |
| `POST /api/sync/upload-file` | sync.send | multipart, field `file`: a `.smapkg` from a USB stick → `UploadResponse { status, batchId, inserted, updated, deleted, notes[], error }` (the batch then shows in `/api/sync/batches`) |

## New in phase 3

### Session
| | | |
|---|---|---|
| `POST /api/auth/sign-out` | none | 204, clears the cookie |

### Dashboard — `dashboard.view`
`GET /api/dashboard?storeId&from&to` →
```
{
  "from": "2026-10-28", "to": "2026-10-28",
  "totals": { "netSales": 0, "tax": 0, "total": 0, "transactions": 0, "averageSale": 0, "units": 0,
              "returns": 0, "discounts": 0, "grossProfit": 0, "voids": 0 },
  "previous": { ...same totals for the same length of time just before `from`... },
  "byStore":   [{ "storeId", "code", "name", "netSales", "transactions", "averageSale", "lastUploadAtUtc" }],
  "byDay":     [{ "date", "netSales", "transactions" }],
  "byHour":    [{ "hour": 0..23, "netSales", "transactions" }],
  "paymentMix":[{ "tender", "amount", "count" }],
  "topItems":  [{ "itemId", "plu", "name", "units", "netSales" }],           // top 10 by net sales
  "byCashier": [{ "userId", "userName", "netSales", "transactions" }],
  "byStation": [{ "storeId", "storeCode", "stationNumber", "netSales", "transactions" }],
  "drawers":   [{ "storeCode", "stationNumber", "closedAt", "closedBy", "expected", "counted", "variance" }]
}
```

### Till monitor — `web.signin`
`GET /api/tills?storeId` → `[{ "stationId", "storeId", "storeCode", "storeName", "number", "name", "enrolled", "retired",
"appVersion", "lastUploadAtUtc", "lastDownloadAtUtc", "lastSaleAt", "todayNetSales", "todayTransactions",
"drawerOpen", "failedBatches", "status" }]`.
`status` is `ok` | `behind` | `failed` | `never` | `retired`.

### Sales — `reports.view`
- `GET /api/sales?storeId&from&to&station&userId&search&status` (*paged*).
  - `status` is `completed` | `voided`.
  - `search` matches the invoice number, the client, or an item code.
  - Each row: `{ "id", "storeCode", "stationNumber", "invoiceNumber", "at", "staff", "client", "units", "netSales",
    "tax", "total", "tenders": "Cash, Visa", "voided" }`.
- `GET /api/sales/{id}`: the row fields, plus:
  - `lines: [{ "lineNumber", "plu", "description", "quantity", "price", "discount", "tax", "total" }]`
  - `payments: [{ "tender", "amount", "currency", "foreignAmount" }]`
  - `voidedBy`, `voidedAt`

### Items — `inventory.view` to read, `inventory.edit` to change
- `GET /api/items?search&departmentId&categoryId&deleted&storeId` (*paged*).
  - Each row: `{ "id", "plu", "name", "department", "category", "price1", "averageCost", "onHand", "isActive" }`.
  - `onHand` is for `storeId`, or the sum over the user's stores.
- `GET /api/items/{id}` returns `ItemDetail`. Its fields:
  - `id`, `version`, `plu`, `name`, `description`
  - `departmentId`, `categoryId`, `supplierId`
  - `price1`, `price2`, `price3`, `lastCost`, `averageCost`
  - `taxable1`, `taxable2`, `taxable3`
  - `reorderPoint`, `reorderQuantity`, `unitsPerCase`, `isActive`, `notes`
  - `stock: [{ "storeCode", "locationCode", "onHand" }]`
- `POST /api/items` and `PUT /api/items/{id}` take `ItemDetail` without `stock` and return the saved `ItemDetail`.
- `DELETE /api/items/{id}` deletes the item; `POST /api/items/{id}/undelete` restores it.
- `GET /api/departments` returns `[{ "id", "name" }]`. `GET /api/categories?departmentId` returns
  `[{ "id", "name", "departmentId" }]`.

### Clients — `clients.view` / `clients.edit`
- `GET /api/clients?search&deleted` (*paged*). Each row: `{ "id", "number", "name", "company", "phone", "email",
  "balance", "points", "isDeleted" }`.
- `GET /api/clients/{id}` returns `ClientDetail`. Its fields:
  - `id`, `version`, `number`
  - `firstName`, `lastName`, `company`
  - `address1`, `address2`, `city`, `province`, `postalCode`, `country`
  - `phone`, `phone2`, `email`
  - `creditLimit`, `balance` (read-only), `points` (read-only)
  - `notes`
- `POST` and `PUT` take a `ClientDetail` and return the saved one. `DELETE` deletes; `POST .../undelete` restores.

### Suppliers — `suppliers.edit` (reading needs `inventory.view`)
- `GET /api/suppliers?search&deleted` (*paged*). Each row: `{ "id", "code", "company", "contact", "phone", "email",
  "isDeleted" }`.
- `GET /api/suppliers/{id}` returns `SupplierDetail`. Its fields:
  - `id`, `version`, `code`, `company`, `contact`
  - `address1`, `address2`, `city`, `province`, `postalCode`, `country`
  - `phone`, `fax`, `email`, `notes`
- `POST`, `PUT`, `DELETE` and `undelete` work as for clients.

### Stock — `inventory.view`
- `GET /api/stock/movements?storeId&itemId&from&to&kind` (*paged*). Each row: `{ "at", "storeCode", "locationCode",
  "plu", "name", "kind", "quantity", "onHandAfter", "unitCost", "documentNumber", "reason", "user" }`.
- `GET /api/transfers?storeId&status&search` (*paged*, newest first). Each row: `{ "id", "number", "status", "fromStore",
  "fromLocation", "toStore", "toLocation", "createdAt", "shippedAt", "receivedAt", "lines", "units", "value" }`.
  - A transfer is shown to both its sending and its receiving store (`StockTransfer.OfStore`).
  - `search` finds the transfer number (all digits), or a location it leaves from or goes to: the location code
    starting with the text, or its name containing it.
- `GET /api/transfers/{id}` adds `lines: [{ "plu", "description", "quantityShipped", "quantityReceived", "unitCost" }]`.
- `GET /api/stock-counts?storeId&status&search` (*paged*, newest first). Each row: `{ "id", "number", "status",
  "storeCode", "locationCode", "startedAt", "postedAt", "lines", "varianceUnits", "varianceValue" }`.
  - `search` finds the count number, or the location counted (code starting with the text, or name containing it).
- `GET /api/stock-counts/{id}` adds `lines: [{ "plu", "description", "expected", "counted", "variance",
  "varianceCost", "notCounted" }]`.

### Gift cards — `giftcards.issue`
- `GET /api/gift-cards?search&active` (*paged*). Each row: `{ "id", "number", "kind", "storeCode", "issuedAt",
  "expiresOn", "isActive", "client", "balance" }`.
- `GET /api/gift-cards/{id}` adds `history: [{ "at", "kind", "amount", "balanceAfter", "storeCode", "stationNumber",
  "invoiceNumber", "user", "note" }]`.

### Reports — `reports.view` (staff reports need `reports.staff`)
- `GET /api/reports` returns the catalog of `ReportService`, filtered to what the user may run: `[{ "key", "group",
  "title", "usesDates" }]`.
- `GET /api/reports/{key}?storeId&from&to` returns
  `{ "title", "subtitle", "columns": [{ "header", "kind", "total" }], "rows": [[...]], "totals": [...] }`.
  - `kind` is the `ColumnKind` name: Text, Money, Quantity, Count, Date, Percent and so on.
- `GET /api/reports/{key}/csv?storeId&from&to` returns the same report as `text/csv`.

### Users and roles — `setup.users`
- `GET /api/permissions` returns `[{ "key", "group", "description" }]`.
- `GET /api/roles` returns `[{ "id", "version", "name", "description", "level", "isSystem", "permissions": [...],
  "userCount" }]`. `POST`, `PUT /api/roles/{id}` and `DELETE` use the same shape, without `userCount`.
- `GET /api/users?storeId` returns `[{ "id", "version", "userName", "fullName", "roleId", "role", "storeId",
  "storeCode", "staffCode", "isActive", "locked", "mustChangePassword", "lastSignInAtUtc" }]`.
- `POST /api/users` takes `{ userName, fullName, roleId, storeId, isActive, password? }`.
- `PUT /api/users/{id}` takes the same body plus `version`.
- `DELETE /api/users/{id}` deletes the user.
- `POST /api/users/{id}/reset-password` returns `{ "temporaryPassword" }`.
- `POST /api/users/{id}/pin` takes `{ staffCode, pin }` and returns 204.
- An administrator without `stores.all` manages only users of their own store, and can neither grant `stores.all` nor
  make a super administrator.

### Audit log — `audit.view`
`GET /api/audit?storeId&from&to&userId&action&search` (*paged*). Each row: `{ "id", "atUtc", "storeCode",
"stationNumber", "userName", "action", "entityName", "entityId", "details", "before", "after" }`. `before` and
`after` are JSON objects or null.

### Time clock and commission — `reports.staff` to read, `timeclock.edit` to change rules
- `GET /api/time-clock?storeId&from&to&userId` returns `[{ "id", "userId", "userName", "storeCode",
  "stationNumber", "inAt", "outAt", "hours", "editedBy", "editReason" }]`.
- `GET /api/time-clock/hours?storeId&from&to` returns `[{ "userId", "userName", "days", "hours" }]`.
- `GET /api/commission?storeId&from&to` returns `[{ "staff", "sales", "units", "netSales", "fromRules",
  "fromItems", "commission" }]`.
- `GET /api/commission-rules` returns `[{ "id", "version", "name", "userId", "userName", "scope", "departmentId",
  "categoryId", "itemId", "target", "percent", "startsOn", "endsOn", "isActive", "basis" }]`. `POST`, `PUT` and
  `DELETE` use the same shape. `target` is display text (what the rule applies to: an item, category or department
  name, or "All items"); the server ignores it on a save. `basis` says how `percent` is read: `PercentOfNetSales` (a
  percentage of the line's net amount, the default) or `AmountPerUnit` (money for every unit sold).

### Store settings and branding — `setup.store`
- `GET /api/stores/{id}/settings` returns `{ "version", "companyName", "address1", "address2", "city", "province",
  "postalCode", "phone", "email", "website", "taxNumber", "currencySymbol", "receiptHeader", "receiptFooter" }`.
- `PUT /api/stores/{id}/settings` saves it. The values flow down to the tills with the master data.
- `GET /api/stores/{id}/images/{kind}` returns `image/jpeg` (404 when there is none). `kind` is `logo` or `watermark`.
- `PUT /api/stores/{id}/images/{kind}` takes a multipart upload with the field `file`. It is resized like the desktop
  app does (`BrandingService`).
- `DELETE /api/stores/{id}/images/{kind}` removes the image.

### RFID hubs — `web.signin` to list, `stores.manage` to change (added in round 2)
An RFID hub can read its store's readers from the server instead of a database (see [RFID.md](RFID.md), "A hub that
reads its readers from the server").
- `GET /api/stores/{id}/rfid-hubs` returns `[RfidHubInfo { "id", "storeId", "name", "keyHint", "createdAtUtc", "createdBy",
  "lastSeenAtUtc", "lastAddress", "revoked", "revokedAtUtc" }]`, the keys in use first. 404 for a store the user cannot see.
- `POST /api/stores/{id}/rfid-hubs` takes `{ name }` (where the hub runs, at most 40 characters) and returns 201
  `{ "hub": RfidHubInfo, "key": "rfh_…" }`. The key is in this answer only; the server keeps its SHA-256.
- `POST /api/rfid-hubs/{id}/revoke` returns the `RfidHubInfo`; the key is refused from then on.
- `GET /api/rfid/readers?hub=` is for the hub itself: no sign-in, the key in the `X-RfidHub-Key` header (401 without it,
  or with a wrong or revoked key). Returns `{ "storeId", "storeCode", "storeName", "hub", "readers": [...] }`: the key's
  store's readers whose hub name is `hub` (every reader of the store without it), each with its antennas and settings.

## Web app pages (phase 4)

The web app is a static export. It has no dynamic route segments: detail pages take `?id=`.

The pages, with the permission each needs:

| Page | Path | Permission |
|---|---|---|
| Sign-in | `/sign-in` | none |
| Change password | `/account` | none; also shown first when `mustChangePassword` |
| Dashboard | `/` | `dashboard.view` |
| Tills | `/tills` | `web.signin`; enrol a till, show its code, retire it |
| Packages | `/packages` | `web.signin`; upload a `.smapkg` from USB, batch history |
| Sales | `/sales` | `reports.view` |
| Sale detail | `/sales/view?id=` | `reports.view` |
| Items | `/items` | `inventory.view` |
| Item editor | `/items/edit?id=` | `inventory.edit` |
| Clients | `/clients` | `clients.view` |
| Suppliers | `/suppliers` | `inventory.view` |
| Stock | `/stock` | `inventory.view`; tabs for movements, transfers and counts |
| Gift cards | `/gift-cards` | `giftcards.issue` |
| Reports | `/reports` | `reports.view` |
| Report | `/reports/view?key=` | `reports.view` |
| Staff | `/staff` | `reports.staff`; time clock, hours and commission |
| Users | `/users` | `setup.users` |
| Roles | `/roles` | `setup.users` |
| Stores | `/stores` | `stores.manage` |
| Settings | `/settings` | `setup.store`; store settings and branding |
| Audit log | `/audit` | `audit.view` |

The navigation shows only the pages the user may open. A store picker sits in the header: it lists every store for
`stores.all` users, and shows the home store fixed for everyone else. It sets `storeId` on every request.

## How the server applies this contract (phase 3)

The rules below were decided while building the server. They add to the contract above and change nothing in it.

### Stores: what a user sees and what they may change

- **Store records** belong to one store: items (through their location), stock movements, stock counts, sales, drawers,
  time clock entries, audit lines, gift cards (the store that issued the card), store settings and pictures, and users
  (their home store). Lists cover the stores the user sees; a `storeId` they cannot see is 403; a single record of a
  store they cannot see is 404, so ids tell nothing about other stores.
- **Transfers** belong to both their sending and their receiving store.
- **Company-wide records** are shared by every store: clients, suppliers, departments, categories, roles and commission
  rules. Anyone with the read permission reads them. **They are changed only by a user with `stores.all`, or by any user
  with the change permission while the company has a single store** (403 otherwise). A store administrator therefore
  edits their own store's items, users, settings and pictures, but not the shared lists, once there is a second store.
- **Users:** an administrator without `stores.all` lists and manages only users of their own store (others are 404),
  cannot make a user of every store (`storeId` null), cannot move a user to another store, and cannot give a role that
  holds `stores.all` nor create or change such a role (403). The Super administrator role holds it.

### Items are kept per location

In the model each location of a store has its own row (and price and stock) for a stock code. So:

- `GET /api/items` lists one row per location item, with two extra fields, `storeCode` and `locationCode`. `onHand` is
  that row's count; with `storeId` the rows are that store's. A user who sees several stores gets one row per store
  that stocks the code.
- `GET /api/items/{id}` is one location's item; its `stock` lists the same code at every location of the stores the
  user sees.
- `POST /api/items` takes an optional extra `locationId`; without it the item goes to the first location of `storeId`,
  else of the user's store, else of the only store (400 when none can be told).
- Extra field `barcode`. `description` is the item's long description and `notes` its message shown at the till.
  `taxable3` is always false (stores have two taxes).
- **A change at every store.** `GET /api/items/{id}/every-store` (`inventory.edit`) returns where it would go:
  `{ "rows": [{ "itemId", "storeCode", "locationCode" }], "skipped": [{ "storeCode", "locationCode", "reason" }] }`, the
  rows of the same stock code at the locations the user may change (a store administrator: their store's; `stores.all`:
  every store's), in store and location order, and those locations where the code is missing or deleted.
  `PUT /api/items/{id}?everyStore=true` takes the same `ItemDetail` as a plain PUT and:
  - saves this row first, its `version` checked as usual (409 changes nothing anywhere);
  - copies to the code's other rows only the fields that the PUT changed on this row: `name`, `description`, `notes`,
    `barcode`, `price1`–`price3`, `lastCost`, `averageCost`, `departmentId`, `categoryId`, `taxable1`, `taxable2`,
    `reorderPoint`, `reorderQuantity`, `isActive` (false deletes them). Supplier, units per case, stock and the stock code
    stay each location's own. Nothing is created; each other row is saved with its own version;
  - writes each row as a change of its own store (audit line under that store, station 0, the web user), so it goes down to
    that store's tills with their next download;
  - answers `{ "item": ItemDetail, "changed", "fields": [...], "changedAt": [{ "itemId", "storeCode", "locationCode" }],
    "skipped": [{ "storeCode", "locationCode", "reason" }] }`: `changed` counts the rows changed, this one included
    (it is first in `changedAt`); `skipped` lists the locations without the code, where it is deleted, where it already
    held the values, or where the change was refused (a barcode used by another item there), with the reason.
- A category is not kept under a department: `departmentId` in category rows is null, and
  `GET /api/categories?departmentId=` returns the categories used by that department's items. Extra endpoints:
  `POST /api/departments` and `POST /api/categories` take `{ name }` (names are kept in capitals);
  `DELETE /api/departments/{id}` and `DELETE /api/categories/{id}`; all four follow the company-wide rule.

### Clients, suppliers, settings

- Client `phone` is the work phone and `phone2` the home phone. A client added on the web without a `number` gets one
  from the server's own number range (station 1000; tills are 1 to 999), so it never clashes with a till's.
- Supplier `contact` is kept as first and last name: the first word is the first name, the rest the last name.
- Store settings: `email`, `website` and `currencySymbol` have no column and are kept as store setting extras (`EMAIL`,
  `WEBSITE`, `CURRENCY`); `receiptHeader` and `receiptFooter` are the store's invoice header and footer texts. Extra
  fields `country` and `fax`. All of it is the store's own rows, so it reaches the store's tills with the next
  download.
- Pictures: any JPEG, PNG, GIF, BMP or WebP up to 20 MB, stored as the desktop app stores them: a JPEG (quality 85) of at
  most 800 pixels a side, drawn on white. Anything else is 400.

### Versions

`version` is an opaque string. Items, clients and suppliers: the row's concurrency token. Roles: the latest change of
the role or of its permissions. Users: the time the user was last edited (signing in does not change it). Commission
rules: their last change. Store settings: the latest change of the settings row, its extras and its receipt texts. A
PUT without `version` is 400; with an old one, 409.

### The audit log on the server

The server writes audit lines exactly as a till does, through the same services: each request works with a
`DataContext.For(StationIdentity(store, 0), web user)`, so the automatic lines (added, changed, deleted, with the values
before and after) and the explicit ones carry:

- the web user's id and name;
- the store the change belongs to: the record's store; for company-wide records the `storeId` of the request, else the
  user's home store, else the first store by code;
- station number **0**, which marks a line written on the web (tills are 1 to 999).

Web sign-ins and failed sign-ins are written the same way, under the user's home store (or the first store). Audit lines
then travel like the tills' own: down to that store's tills.

### Dates, figures and statuses

- Business dates (`from`, `to`) are the store's days; without a `storeId` and for several stores, the server's own date.
  A `from` after `to` is turned round; a date that is not `yyyy-MM-dd` is 400.
- `GET /api/audit` takes `from` and `to` as the store's business days like every other list; the server turns them
  into UTC times with the store's time zone, as the log keeps UTC times. `GET /api/stock/movements` without `from` and
  `to` covers all time.
- Every date-time whose name ends in `Utc` is written with a `Z`; the others (the tills' local times) have no offset.
- Dashboard: `netSales` is completed sales after line and sale discounts, before taxes; voided sales are left out and
  counted in `voids`; `returns` is the value of returned lines (a positive number); `discounts` adds line and sale
  discounts; `grossProfit` is net sales less the cost of the goods; `previous` covers the same number of days just
  before `from`. `drawers` are the drawers closed in the period with the cash expected (opening float, cash taken,
  pay-ins less pay-outs); tills do not record a counted amount, so `counted` and `variance` are null.
- Till `status`: `retired`; `never` (not joined, or nothing sent yet); `failed` (a package refused since the last good
  one); `behind` (nothing sent for `Server:BehindAfterHours`, 24 by default); else `ok`.
- Reports run over every location of the stores in scope; the reports that are not kept per location (drawers, aged
  receivables, hours, commission, gift card liability) cover the same stores. The CSV is UTF-8 with a byte order mark,
  named `{key}-{from}-{to}.csv`.

### Added after the web app was built

- `UserInfo.canChangeShared`: true when the user has `stores.all` or the company has one store, the same rule the
  server applies to company-wide records; the web app hides those records' edit buttons when it is false.
- `StoreInfo.currencySymbol`: the store's currency symbol (Setup), readable by every signed-in user.
- `GET /api/stores/{id}/locations` (`web.signin`; 404 for a store the user cannot see) → `[{ "id", "code", "name" }]`,
  for the location a new item goes to.
- `GET /api/staff?storeId` → `[{ "id", "userName", "fullName" }]`, sorted by user name, active or not: the users of that
  store and those who work at every store (every user for `stores.all` without `storeId`). Allowed with any of
  `reports.view`, `reports.staff`, `audit.view` or `setup.users`, for the staff filters of the lists.

### Session, errors and the web app's files

- The cookie lasts as long as the token (`Jwt:Hours`, 12). The CSRF guard applies to requests signed in by the cookie;
  sign-in and sign-out (open to anyone) and requests with an `Authorization` header are not checked.
- Errors are always `{ "error" }`. A change the database refuses (a code that must be unique, a record that no longer
  exists) is 400. An unknown address under `/api` is a JSON 404.
- The server serves the web app's static export from `Server:WebRoot` (default: `web/out`, looked for in the content root
  and the folders above it; when there is none, only the API is served). `/x/` serves `x/index.html` and `/x` serves
  `x.html`; any other path outside `/api` gets the export's `404.html` with status 404. Files under `/_next/static/` are
  cached for a year (`immutable`), pages are `no-cache`. Nothing under `/api` is ever served from the files.

## HR and payroll (phase E3)

Employees, shifts and attendance, leave, and payroll, all kept on the server ([ERP_E3_HR.md](ERP_E3_HR.md)). The
conventions above hold: `storeId` limits a list to one store, a change sends back the `version` it read (409 when it is
old), refused business rules are 400 with `{ "error" }`. Employees belong to their store; one of head office (no
store), a run of the whole company and the shared HR lists (departments, designations, rule sets, leave types,
holidays, and shifts for every store) follow the rule for company-wide records: `canChangeShared`. An employee, run,
payslip or loan of a store the user cannot see is 404.

### HR lists and settings: `hr.view` to read, `hr.edit` to change

- `GET /api/hr/departments`, `GET /api/hr/designations` → `[{ "id", "version", "name", "isActive", "employees" }]`;
  `POST`, `PUT /{id}`, `DELETE /{id}` (refused while employees are in it).
- `GET /api/hr/settings` → `{ "companyCountry", "countries", "stores": [{ "storeId", "code", "name", "country", "own" }] }`;
  `PUT /api/hr/stores/{id}/country` `{ "country" }` (two letters; null goes back to the company's).
- `GET /api/hr/rule-sets?country`, `GET /api/hr/rule-sets/{id}` → `RuleSetInfo`: the country, name, `effectiveFrom`,
  `effectiveTo`, `taxYearStartMonth`, `minimumWage`, EOBI and social security rates, ceiling and basis (`Wage`,
  `MinimumWage`), the working week, overtime, `dailyRateBasis` (`CalendarDays`, `ThirtyDays`, `WorkingDays`),
  `latenessRule` (`None`, `DayPerLates`, `PerMinute`), surcharge, gratuity, CNIC and IBAN patterns with examples,
  `notes`, `taxSlabs` `[{ "incomeFrom", "incomeTo", "fixedTax", "rate" }]` and `allowances`
  `[{ "code", "name", "isTaxable", "exemptPercentOfBasic", "sortOrder" }]`. `POST`, `PUT /{id}` (slabs and allowances
  are replaced as sent), `DELETE /{id}` (refused when an approved run falls in its period). The rule set used for a
  month is the latest of the country that has started by the month's last day and has not ended.
- `GET /api/hr/leave-types?country` → `[{ "id", "version", "country", "code", "name", "kind", "daysPerYear", "isPaid",
  "accruesMonthly", "carryOverMaxDays", "encashable", "countsCalendarDays", "onlyFor", "minimumServiceMonths",
  "isActive", "sortOrder" }]`; `POST`, `PUT /{id}`, `DELETE /{id}` (refused once used).
- `GET /api/hr/holidays?country&year` → `[{ "id", "version", "country", "date", "name" }]`; `POST`, `PUT /{id}`,
  `DELETE /{id}`.

### Employees: `hr.view` to read, `hr.edit` to change

- `GET /api/hr/employees?search&departmentId&designationId&status&storeId&page&pageSize` → a page of
  `{ "id", "code", "fullName", "designation", "department", "storeId", "storeCode", "employmentType", "joiningDate",
  "leavingDate", "active", "phone", "userName" }`. `status`: `active` (default), `left` or `all`. `search` looks in
  the code, name, CNIC and phone.
- `GET /api/hr/employees/next-code` (`hr.edit`) → `{ "code" }` (E0001, E0002…).
- `GET /api/hr/employees/{id}` → `EmployeeDetail`: every field of the record, the manager's and user's names, and the
  `country` whose rules apply. `POST /api/hr/employees` (201) and `PUT /api/hr/employees/{id}` take the same shape; the
  CNIC and IBAN must match the country's rule set (and the IBAN its check digits), a salary paid by bank needs the IBAN,
  a user is linked to one employee, and nobody reports to themselves. `DELETE` is refused for someone who was paid or
  has loans: give a leaving date instead.
- `GET /api/hr/employees/{id}/documents` → `[{ "id", "employeeId", "title", "fileName", "contentType", "size",
  "uploadedAtUtc", "uploadedBy" }]`; `POST` the same address as a form with `file` and `title` (at most 10 MB);
  `GET /api/hr/documents/{id}` gives the file; `DELETE /api/hr/documents/{id}`.

### Shifts, roster and attendance: `hr.view` to read

- `GET /api/hr/shifts?storeId` → `[{ "id", "version", "name", "startTime", "endTime", "breakMinutes", "graceMinutes",
  "days", "storeId", "isActive" }]` (`HH:mm`; `days` are `Monday` … `Sunday`; an end before the start is the next day).
  `POST`, `PUT /{id}`, `DELETE /{id}` with `hr.edit`.
- `GET /api/hr/roster?week&storeId` → `{ "weekStart", "days", "rows": [{ "employeeId", "code", "name", "storeId",
  "usualShiftId", "weekShiftId", "changes" }] }`. With `hr.attendance`: `PUT /api/hr/roster`
  `{ "weekStart", "assignments": [{ "employeeId", "shiftId" }] }` (no shift: back to the usual one),
  `POST /api/hr/roster/changes` `{ "employeeId", "date", "shiftId", "note" }` (no shift: a day off),
  `DELETE /api/hr/roster/changes/{id}`.
- `GET /api/hr/attendance?from&to&storeId&employeeId` (at most 93 days) → `[{ "employeeId", "code", "name", "date",
  "status", "shift", "shiftStart", "shiftEnd", "firstIn", "lastOut", "workedMinutes", "lateMinutes", "earlyMinutes",
  "overtimeMinutes", "holiday", "leaveCode", "leaveDays", "corrected" }]`. `status`: `Present`, `Absent`, `Leave`,
  `Holiday`, `Off`, `Scheduled` (still to come). Worked out from the time clock when asked.
- `GET /api/hr/attendance/sheet?month=yyyy-MM&storeId` → `{ "month", "days", "rows": [{ …, "marks", "present", "late",
  "leftEarly", "absent", "leave", "holidays", "off", "overtimeHours" }] }`.
- `GET /api/hr/attendance/corrections?status&from&to&storeId`; with `hr.attendance`: `POST` `{ "employeeId", "date",
  "status", "workedMinutes", "lateMinutes", "earlyMinutes", "overtimeMinutes", "reason" }` (201),
  `POST /{id}/approve` and `/{id}/reject` `{ "note" }`, by someone other than who asked.

### Leave: `hr.view`

- `GET /api/hr/leave-requests?status&employeeId&from&to&storeId&page&pageSize` → a page of `{ "id", "employeeId",
  "code", "name", "leaveTypeId", "leaveType", "fromDate", "toDate", "firstDayHalf", "lastDayHalf", "days", "reason",
  "status", "requestedBy", "requestedAtUtc", "decidedBy", "decidedAtUtc", "decisionNote", "mayDecide" }`.
- `POST /api/hr/leave-requests` `{ "employeeId", "leaveTypeId", "fromDate", "toDate", "firstDayHalf", "lastDayHalf",
  "reason" }` (201): refused when it overlaps other leave, does not fit the employee (gender, service) or takes more
  than is available.
- `POST /api/hr/leave-requests/{id}/approve`, `/reject`, `/cancel` `{ "note" }`: the employee's manager or `hr.leave`,
  never the employee; the one who asked may cancel.
- `GET /api/hr/leave-balances?year&employeeId&storeId` → `[{ "employeeId", "code", "name", "leaveTypeId", "leaveCode",
  "leaveType", "paid", "limited", "entitled", "earned", "carriedIn", "adjusted", "taken", "pending", "available" }]`.
- `POST /api/hr/leave-adjustments` (`hr.leave`) `{ "employeeId", "leaveTypeId", "year", "days", "reason" }`.

### Payroll: read with `payroll.run` or `payroll.approve`

Salaries, loans, runs, payslips and the payroll reports are 403 for anyone else, whatever else their role has.

- `GET /api/hr/employees/{id}/salary` → `[{ "id", "effectiveFrom", "basic", "note", "changedBy", "components":
  [{ "kind", "code", "name", "amount", "accountCode" }], "gross", "fixedDeductions" }]`, the latest first;
  `POST` (`payroll.run`, 201) records a salary from a date (the same date replaces it; refused inside an approved
  month); `DELETE /api/hr/salary/{id}`. `kind` is `Allowance` or `Deduction`; `accountCode` is the ledger account a
  deduction is owed to.
- `GET /api/payroll/loans?employeeId&status&storeId`, `GET /api/payroll/loans/{id}` → `{ "id", "number",
  "employeeId", "code", "name", "kind", "issuedOn", "amount", "instalment", "firstMonth", "status", "recovered",
  "outstanding", "reason", "paidFromAccount", "instalments" }`. `POST /api/payroll/loans` (`payroll.run`)
  `{ "employeeId", "kind", "issuedOn", "amount", "instalment", "firstMonth", "reason", "paidFrom" }` posts the payment
  to the ledger (`paidFrom`: `Cash`, `Bank` or an account code); `POST /{id}/cancel` `{ "reason" }` before anything was
  taken back.
- `GET /api/payroll/runs?year&storeId` → `RunRow[]`: `id`, `version`, `number`, `kind` (`Monthly`, `FinalSettlement`),
  `year`, `month`, `title`, the period, store, employee, `status` (`Draft`, `Calculated`, `Approved`, `Posted`,
  `Paid`), `employees`, `gross`, `deductions`, `net`, `tax`, `employerContributions`, `notes`, `warnings`, who did each
  step and when, `journalNumber`, `paidOn`, `paymentJournalNumber`.
- `POST /api/payroll/runs` (`payroll.run`) `{ "year", "month", "storeId", "notes" }` (201; no `storeId` is the whole
  company, for those who see every store; one run per month and store). `POST /api/payroll/settlements`
  `{ "employeeId", "notes" }` starts a leaver's final settlement. `DELETE /api/payroll/runs/{id}` while draft or
  calculated.
- Steps, each `{ "version" }` → the run: `POST /api/payroll/runs/{id}/calculate` and `/post` (`payroll.run`),
  `/approve` and `/reopen` (`payroll.approve`), `/pay` (`payroll.run`) `{ "version", "paidOn", "bankAccount",
  "cashAccount" }` (default today and the Bank and Cash accounts). Calculating notes on the run the employees left out
  (no salary, or no rule set for the month).
- `GET /api/payroll/runs/{id}` → `{ "run", "payslips" }`, each payslip with the days, earnings by kind, deductions by
  kind, net and the employer's part. `GET /api/payroll/payslips/{id}` → the payslip with its lines and the tax
  projection (`taxableBefore`, `taxBefore`, `projectedAnnualTaxable`, `projectedAnnualTax`).
- `GET /api/payroll/runs/{id}/bank-transfers` → CSV (code, employee, CNIC, bank, IBAN, amount, reference);
  `GET /api/payroll/runs/{id}/cash-sheet` → `{ "run", "rows": [{ "code", "name", "designation", "net" }], "total" }`.

### HR reports: `hr.view`

- `GET /api/hr/reports` → `[{ "key", "group", "title", "parameters" }]`; the payroll ones only for payroll's users.
- `GET /api/hr/reports/{key}` → the report table as for `/api/reports`; `/csv` gives it as a file. Keys and parameters:
  `salary-sheet` (`runId`), `tax-certificate` (`employeeId`, `taxYear`: the calendar year it starts in),
  `eobi` and `social-security` (`month=yyyy-MM`), `attendance` (`month`), `leave`, `overtime` and `headcount`
  (`from`, `to`); all but the first two take `storeId`.

### Also changed

- `GET /api/staff` is allowed with `hr.view` or `hr.edit` too, for the user picker on the employee page.
- New permission keys `hr.view`, `hr.edit`, `hr.attendance`, `hr.leave`, `payroll.run` and `payroll.approve`, and the
  standard role HR officer.
- Nothing of HR or payroll goes to the tills, nor do the audit lines about it or about ledger entries.
