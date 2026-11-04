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

## Supply chain (phase E2)

Added with the supply chain ([ERP_E2_SUPPLY.md](ERP_E2_SUPPLY.md)); the server code is `Endpoints.Supply.cs` and the web
types are in `web/src/lib/types.ts`. The usual rules hold: lists take `storeId` (403 for a store the user cannot see),
a record of another store is 404, changes send the `version` they read (409 when it changed), and they are written as the
web user at station 0 of the record's store. Where an endpoint needs a location and none is given (`locationId` in the
query or the body), it takes the first location of `storeId`, else of the user's store, else of the only store.

### Batches and expiry — `inventory.batches`
- `GET /api/batches?storeId&locationId&search&inStock` (*paged*, by stock code, then first expiry first). `inStock`
  defaults to true. Each row is a `BatchRow`: `{ "id", "version", "itemId", "plu", "itemName", "storeCode",
  "locationCode", "batchNumber", "expiresOn", "madeOn", "supplier", "receivedOn", "purchaseOrderNumber", "onHand",
  "unitCost", "value", "daysLeft", "bin", "note" }`. `onHand` is the sum of the batch's stock movements; `daysLeft` is
  negative once it has expired.
- `GET /api/batches/expiring?storeId&days` and `GET /api/batches/expired?storeId` return
  `{ "days", "rows": [BatchRow], "units", "value" }`: batches with stock that expire from today to `days` ahead (default:
  the store's `expiryWarningDays`, 30 across several stores), soonest first; or that have expired, oldest first
  (`days` 0).
- `GET /api/items/{id}/batches` returns `[BatchRow]` of one item, in the order they leave (first expiry first).
- `POST /api/batches` takes `{ itemId, batchNumber, expiresOn, madeOn, supplierId, note }` (201, the `BatchRow`).
  `PUT /api/batches/{id}` takes the same with `version` (the item stays; a batch with movements keeps its number). 400
  for an item not kept in batches or a number the item already has.
- `POST /api/batches/{id}/adjust` takes `{ quantity, setTo, reason }`: adds `quantity`, or with `setTo` makes the batch
  hold it, as an adjustment movement of the batch. It also needs `inventory.adjust`. Returns the `BatchRow`.

### Bins and their lists — `inventory.bins`
- `GET /api/bins?storeId&locationId` returns `[{ "id", "version", "locationId", "storeCode", "locationCode", "code",
  "zone", "description", "isActive", "items" }]`; `items` counts the items whose default bin it is.
- `POST /api/bins` takes `{ locationId, code, zone, description, isActive }` (201; codes are kept in capitals and are
  unique per location). `PUT /api/bins/{id}` takes the same with `version`; a new code moves its items with it.
  `DELETE /api/bins/{id}` is 204, or 400 while items are kept in it.
- `POST /api/bins/assign` takes `{ itemIds: [...], binCode }` (items of one store; `binCode` null takes them out of
  their bin) and returns `{ "changed" }`.
- `GET /api/bins/count-sheet?locationId&zone&bin`, `GET /api/bins/pick-orders?locationId`, `GET /api/transfers/{id}/pick`
  and `GET /api/purchase-orders/{id}/put-away` (that one with `purchasing.manage`) return
  `{ "title", "rows": [{ "zone", "bin", "plu", "description", "quantity", "batchNumber", "expiresOn", "document",
  "onHand" }] }`, sorted by zone and bin, items without a bin last. Pick lists name the batch to take (first expiry
  first); the put-away list names the batches that arrived.

### Purchase orders — `purchasing.manage`
- `GET /api/purchase-orders?storeId&status&search` (*paged*, newest first). `status` is `Draft`, `Posted` or `Received`;
  `search` finds the order number, its description or a supplier. Each row: `{ "id", "number", "storeCode",
  "locationCode", "status", "createdAt", "postedAt", "receivedAt", "expectedOn", "supplier", "description", "lines",
  "totalCost" }`.
- `GET /api/purchase-orders/{id}`: the row's fields with `version`, `storeId`, `locationId`, `supplierId`, `createdBy`,
  `receivedBy` and `lines: [{ "id", "itemId", "plu", "description", "supplier", "caseQuantity", "orderQuantity",
  "pieces", "costEach", "orderedCost", "orderCost", "quantityReceived", "tracksBatches", "shelfLifeDays", "bin",
  "batches": [{ "batchNumber", "pieces", "expiresOn", "madeOn" }] }]`. `orderedCost` is the cost agreed when the order
  was posted, which the three-way match compares the bill with.
- `POST /api/purchase-orders/{id}/post` takes `{ version }`: the pieces go on order. Returns the order.
- `POST /api/purchase-orders/{id}/receive` takes `{ version, shippingCost, lines: [{ lineId, pieces, costEach, batches }] }`
  and returns the order, received. A line of an item kept in batches needs `batches` (`[{ batchNumber, pieces,
  expiresOn, madeOn }]`) adding up to its pieces (400 otherwise); a batch without `expiresOn` gets one from the item's
  shelf life. Stock, last cost and average cost change as at the till.

### Requisitions — `purchasing.manage`; approving and rejecting need `purchasing.approve`
- `GET /api/requisitions?storeId&locationId&status` (*paged*, newest first). `status` is `Draft`, `Submitted`,
  `Approved`, `Rejected` or `Ordered`. Each row: `{ "id", "number", "storeCode", "locationCode", "status", "createdAt",
  "requestedBy", "neededBy", "lines", "total", "decidedBy", "decidedAt", "note" }`.
- `GET /api/requisitions/{id}`: `{ "id", "version", "number", "storeId", "storeCode", "locationId", "locationCode",
  "status", "createdAt", "requestedBy", "neededBy", "note", "submittedAt", "decidedAt", "decidedBy", "decisionNote",
  "orderedAt", "total", "lines": [{ "id", "lineNumber", "itemId", "plu", "description", "quantity", "costEach", "value",
  "neededBy", "supplierId", "supplier", "note", "purchaseOrderId", "purchaseOrderNumber" }] }`. `total` is at the
  expected costs (the item's last cost unless given), which the approver's limit is checked against.
- `POST /api/requisitions?storeId` takes `{ locationId, neededBy, note, lines: [{ itemId, plu, quantity, neededBy,
  supplierId, costEach, note }] }` (201, the detail). A line names its item by `itemId` or by `plu` at the location; the
  supplier is the one given, else the item's.
- `PUT /api/requisitions/{id}` takes the same with `version`; the lines replace the old ones. Only a draft or a rejected
  requisition changes (a rejected one becomes a draft again). `DELETE` deletes a draft or rejected one.
- `POST /api/requisitions/{id}/submit`, `/approve` and `/reject` take `{ version, note }` and return the detail. A
  rejection needs a `note` (why). Approving is refused (400) above the role's limit, or by the person who asked when the
  store's `approverMustDiffer` is set.
- `POST /api/requisitions/{id}/order` takes `{ version }`: the approved lines become draft purchase orders, one per
  supplier. Returns `{ "orders": [{ "id", "number", "supplier" }] }`.
- `GET /api/supply/approval-limits` (`purchasing.approve`) returns `[{ "roleId", "role", "mayApprove", "maxAmount" }]`;
  `maxAmount` null is no limit. `PUT /api/supply/approval-limits/{roleId}` (`setup.users`) takes `{ maxAmount }` and
  returns the list. Limits hold for every store, so they follow the company-wide rule.

### Three-way match — `purchasing.manage`; deciding needs `purchasing.approve`
- `GET /api/matching?storeId&status&purchaseOrderId` (*paged*, held lines first, then newest bill). `status` is
  `Matched`, `Held`, `Accepted` or `Rejected`. Each row: `{ "id", "version", "storeCode", "purchaseOrderId",
  "purchaseOrderNumber", "purchaseOrderLineId", "billId", "billLineId", "billNumber", "billDate", "supplier", "plu",
  "description", "quantityOrdered", "quantityReceived", "quantityBilled", "quantityMatched", "orderedCost",
  "receivedCost", "billedCost", "priceDifference", "status", "reason", "checkedAtUtc", "decidedAtUtc", "decidedBy",
  "decisionNote" }`. `priceDifference` is `quantityMatched × (billedCost − receivedCost)`.
- `POST /api/matching/run?storeId` checks again every order of the store (or of the stores the user sees) that a supplier
  bill names, and returns `{ "lines", "matched", "held", "posted" }`.
- `POST /api/matching/{id}/accept` and `/reject` take `{ version, note }` (a rejection needs the `note`) and return the
  row. Only a held line is decided. Accepting posts its price difference.
- `GET /api/matching/received-not-billed?storeId` and `GET /api/matching/billed-not-received?storeId` return
  `[{ "purchaseOrderId", "purchaseOrderNumber", "purchaseOrderLineId", "storeCode", "plu", "description", "supplier",
  "ordered", "received", "billed", "difference", "costEach", "value", "receivedAt" }]`.
- The bills come from the payables module through `IBillLineSource` (see ERP_E2_SUPPLY.md, *The seam to E1*). The store's
  tolerances are `quantityTolerancePercent`, `quantityToleranceUnits`, `priceTolerancePercent` and
  `priceToleranceAmount` of the supply settings: a difference passes when it is within the larger of the two.

### Landed costs — `purchasing.manage` to read, `purchasing.approve` to make, change, post and delete
- `GET /api/landed-costs?storeId&status` (*paged*, newest first). `status` is `Draft` or `Posted`. Each row: `{ "id",
  "number", "storeCode", "date", "status", "basis", "reference", "total", "receipts", "note", "postedAtUtc",
  "postedBy" }`. `basis` is `Value`, `Quantity` or `Weight`.
- `GET /api/landed-costs/receipts?storeId` returns the received orders a voucher can be spread over: `[{
  "purchaseOrderId", "number", "storeCode", "locationCode", "receivedAt", "supplier", "description", "lines",
  "value" }]`.
- `GET /api/landed-costs/{id}` returns `{ "id", "version", "number", "storeId", "storeCode", "date", "status", "basis",
  "reference", "note", "total", "charges": [{ "kind", "description", "amount" }], "receipts": [{ "purchaseOrderId",
  "number", "receivedAt", "supplier", "value" }], "allocations": [{ "purchaseOrderLineId", "itemId", "plu",
  "description", "quantity", "value", "weight", "share", "onHand", "toInventory", "toCostOfGoods" }], "createdBy",
  "postedAtUtc", "postedBy" }`. `kind` is `Freight`, `Duty`, `Insurance` or `Other`. A draft's `allocations` are empty;
  `GET /api/landed-costs/{id}/preview` works them out as posting would.
- `POST /api/landed-costs` takes `{ storeId, date, basis, reference, note, charges, purchaseOrderIds }` (201, the detail
  with the preview). `PUT /api/landed-costs/{id}` takes the same with `version`, for a draft.
- `POST /api/landed-costs/{id}/post` takes `{ version }` and returns the posted voucher: the share of the pieces still
  on hand raises the items' average cost, the rest goes to cost of goods, in one ledger entry. `DELETE` deletes a draft
  (400 once posted).

### Reorder suggestions — `purchasing.manage`
- `GET /api/reorder?storeId&locationId&supplierId&all` returns `{ "locationId", "rows": [{ "itemId", "plu", "name",
  "supplierId", "supplier", "onHand", "onOrder", "customerOrdered", "unitsSold", "averageDailySales", "leadTimeDays",
  "reorderPoint", "reorderLevel", "targetStock", "caseQuantity", "minimumOrder", "suggestedPieces", "orderQuantity",
  "costEach", "value" }] }`: the items that need ordering now (every item with `all=true`), by supplier and stock code.
- `POST /api/reorder/orders?storeId` takes `{ locationId, lines: [{ itemId, pieces, supplierId }] }` and returns
  `{ "orders": [{ "id", "number", "supplier" }] }`: draft purchase orders, one per supplier, in whole cases.
- `POST /api/reorder/requisition?storeId` takes `{ locationId, lines, submit, neededBy }` and returns the requisition
  (201), sent for approval when `submit` is true.

### Supplier performance — `reports.view`
`GET /api/supplier-performance?storeId&from&to` (default: the last 90 days) returns `{ "from", "to", "rows": [{
"supplierId", "supplier", "orders", "onTime", "onTimePercent", "orderedPieces", "receivedPieces", "fillRatePercent",
"priceIncreases", "priceDecreases", "averagePriceChangePercent", "returns", "returnedPieces", "returnedValue",
"averageLeadDays", "receivedValue" }] }`. Percentages are null where there is nothing to work them out from.

### Supply chain settings — `purchasing.manage` to read, `setup.store` to change
`GET /api/supply/settings?storeId` returns `{ "version", "storeId", "blockExpiredSales", "expiryWarningDays",
"quantityTolerancePercent", "quantityToleranceUnits", "priceTolerancePercent", "priceToleranceAmount",
"salesWindowDays", "safetyStockDays", "orderCoverDays", "defaultLeadTimeDays", "approverMustDiffer" }` (`version` is
`new` while the store has none saved; the defaults then apply). `PUT /api/supply/settings?storeId` saves it. A store the
user cannot see is 404. The settings go down to the store's tills, whose POS reads `blockExpiredSales`.
