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

## New in phase E4: sales, CRM and marketing

Permissions: `crm.view` (read leads, opportunities and activities; mark one's own activities done), `crm.edit` (change
them, convert leads, send a single WhatsApp message), `marketing.campaigns` (segments, WhatsApp templates and
campaigns), `marketing.promotions` (promotions), `sales.targets` (targets). Supervisors and above have `crm.view`;
Managers, Administrators and the new **Marketing** role have the others (Marketing has no `sales.targets`).

**Store scope.** Leads, opportunities, activities, campaigns, WhatsApp messages and targets belong to one store and
follow the store rules of phase 3 (lists cover the stores the user sees; another store's record is 404). Without a
`storeId` in the body a new record goes to the store asked for, else the user's store, else the first store. Stages and
WhatsApp templates are company-wide (changed under the company rule: `stores.all`, or a single store). A segment belongs
to the store its rules name (`rules.storeId`) or, without one, to the company (company rule for changes). A promotion
for one store may be changed by that store's users; one for every store or several stores follows the company rule.

### Pipeline stages — `crm.view` / `crm.edit`
- `GET /api/crm/stages` → `[{ "id", "name", "sortOrder", "probability", "outcome" }]`, in board order. `outcome` is
  `Open`, `Won` or `Lost`. A new database has New (10), Qualified (30), Proposal (60), Won (100) and Lost (0).
- `PUT /api/crm/stages` takes the whole list `[{ "id"?, "name", "probability", "outcome" }]` in board order and returns
  it saved. It needs an Open, a Won and a Lost stage; a stage left out that still holds opportunities is 400.

### Leads — `crm.view` / `crm.edit`
- `GET /api/leads?storeId&ownerId&status&search&from&to` (*paged*, newest first). `status` is `Open`, `Converted` or
  `Dropped`; `search` matches name, company, phone, WhatsApp number or e-mail; `from`/`to` (only when given) the day it
  was added. Row `LeadRow`: `{ "id", "version", "name", "company", "phone", "whatsAppNumber", "email", "source",
  "ownerId", "ownerName", "storeId", "storeCode", "status", "clientId", "clientName", "notes", "createdAtUtc",
  "convertedAtUtc" }`.
- `GET /api/leads/{id}` → `LeadRow`. `POST /api/leads` → 201 `LeadRow`; `PUT /api/leads/{id}` → `LeadRow`. Body
  `{ "name", "company"?, "phone"?, "whatsAppNumber"?, "email"?, "source"?, "ownerId"?, "storeId"?, "notes"?, "status"?,
  "version" (PUT) }`. The owner defaults to the signed-in user. A WhatsApp number is kept in international form
  (`+923001234567`; spaces and dashes are dropped, `00` becomes `+`); a number without a country code is 400.
- `DELETE /api/leads/{id}` → 204, with its activities; 400 while opportunities point at it.
- `POST /api/leads/{id}/convert` (`crm.edit` and `clients.edit`) takes `{ "clientId"?, "version" }`: the lead becomes
  that client, or a new client made from the lead (numbered from the server's range). Its opportunities and activities
  move to the client. → `{ "lead": LeadRow, "clientId", "clientNumber" }`. A converted lead is 400.

### Opportunities — `crm.view` / `crm.edit`
- `OpportunityRow`: `{ "id", "version", "title", "clientId", "clientName", "leadId", "leadName", "stageId", "stage",
  "outcome", "expectedValue", "probability", "weightedValue", "expectedCloseOn", "ownerId", "ownerName", "storeId",
  "storeCode", "lostReason", "closedOn", "notes", "createdAtUtc" }`. `weightedValue` = value × probability / 100.
- `GET /api/opportunities?storeId&ownerId&stageId&outcome&search&from&to` (*paged*). `from`/`to` (only when given)
  filter the expected close date of open opportunities and the close date of won and lost ones.
- `GET /api/opportunities/board?storeId&ownerId&from&to` → `{ "stages": [{ "id", "name", "outcome", "probability",
  "count", "value", "weighted", "opportunities": OpportunityRow[] }] }`, every stage in board order. Without dates it
  holds every open opportunity and those won or lost in the last 30 days.
- `GET /api/opportunities/{id}`; `POST` (201), `PUT /{id}`, `DELETE /{id}` (204, with its activities). Body `{ "title",
  "clientId"?, "leadId"?, "stageId"?, "expectedValue", "probability"?, "expectedCloseOn"?, "ownerId"?, "storeId"?,
  "lostReason"?, "notes"?, "version" (PUT) }`: one of `clientId` and `leadId`. The stage defaults to the first open
  stage, the probability to the stage's.
- `POST /api/opportunities/{id}/move` takes `{ "stageId", "version", "lostReason"? }` (the board's drag) and returns the
  row. A Lost stage needs a reason. Won and Lost set `closedOn` to today; moving back to an open stage clears it. The
  probability becomes the stage's.
- `GET /api/opportunities/report?storeId&ownerId&from&to` (closed in the period, default today) → `{ "from", "to",
  "won": { "count", "value" }, "lost": { "count", "value" }, "winRate", "byOwner": [{ "ownerId", "ownerName", "won",
  "wonValue", "lost", "lostValue" }], "lostReasons": [{ "reason", "count", "value" }], "closed": OpportunityRow[] }`.
  `winRate` is won ÷ (won + lost) × 100, 0 when nothing closed.

### Activities and my tasks — `crm.view` / `crm.edit`
- `ActivityRow`: `{ "id", "version", "kind", "subject", "notes", "clientId", "clientName", "leadId", "leadName",
  "opportunityId", "opportunityTitle", "dueAt", "isDone", "doneAtUtc", "ownerId", "ownerName", "storeId", "storeCode",
  "due", "createdAtUtc" }`. `kind` is `Call`, `Visit`, `Meeting`, `Note` or `Task`; `dueAt` is the store's local time
  (`yyyy-MM-ddTHH:mm:ss`) or null; `due` is `overdue`, `today`, `later`, `none` (no due date) or `done`.
- `GET /api/activities?storeId&ownerId&clientId&leadId&opportunityId&kind&done&from&to` (*paged*: open ones first, by
  due time; `from`/`to` on the due day, only when given).
- `POST /api/activities` (201), `PUT /api/activities/{id}`, `DELETE /api/activities/{id}`. Body `{ "kind", "subject",
  "notes"?, "clientId"?, "leadId"?, "opportunityId"?, "dueAt"?, "ownerId"?, "storeId"?, "isDone"?, "version" (PUT) }`:
  at least one of client, lead and opportunity. The owner defaults to the signed-in user.
- `POST /api/activities/{id}/done` takes `{ "done", "version" }` → `ActivityRow`. `crm.view` is enough for one's own
  activities; others' need `crm.edit`.
- `GET /api/activities/mine?due=` → `{ "overdue", "dueToday", "items": ActivityRow[] }`: the signed-in user's open
  activities, overdue first (oldest first), then today's, later ones, and those without a date. `due=today` keeps only
  overdue and today's (the dashboard's section). "Today" is the day of the user's store.

### Customer segments — `marketing.campaigns`
- `SegmentRules` (every field optional; a segment matches the clients that meet all that are set): `{ "periodDays",
  "from", "to", "minSpend", "maxSpend", "minVisits", "maxVisits", "lastPurchaseBefore", "lastPurchaseAfter",
  "noPurchaseForDays", "storeId", "departmentId", "birthdayMonth", "minPoints", "maxPoints", "minBalance",
  "whatsAppConsent" }`.
  - Spend (the totals the client paid, returns taken off) and visits (completed sales) count the last `periodDays`
    days, else `from`–`to`, else all time; with `storeId`, that store's sales only, and the last purchase there.
  - `lastPurchaseBefore` / `noPurchaseForDays` find lapsed clients (a client who never bought counts as lapsed);
    `lastPurchaseAfter` recent ones. `departmentId`: bought something of that department in the period.
  - `birthdayMonth` 1–12; points and balance owing are the client's now; `whatsAppConsent` true keeps the clients who
    agreed and have a WhatsApp number, false those who did not.
- `SegmentRow`: `{ "id", "version", "name", "description", "rules", "summary", "storeId", "lastCount",
  "lastEvaluatedAtUtc" }`. `summary` is the rules in words.
- `GET /api/segments`, `GET /api/segments/{id}`; `POST` (201), `PUT /{id}`, `DELETE /{id}` (400 while a campaign uses
  it). Body `{ "name", "description"?, "rules", "version" (PUT) }`.
- `POST /api/segments/preview` takes `{ "rules" }`; `POST /api/segments/{id}/evaluate` re-evaluates a saved segment
  and keeps its count. Both → `{ "count", "evaluatedAtUtc", "members": SegmentMember[] }` with the first 200 members
  (biggest spenders first): `{ "clientId", "number", "name", "phone", "email", "whatsAppNumber", "whatsAppOptIn",
  "spend", "visits", "lastPurchase", "points", "balance", "birthday" }`.
- `GET /api/segments/{id}/csv` → every member as `text/csv` (UTF-8 with a byte order mark), `segment-{name}.csv`.

### WhatsApp templates, campaigns and messages — `marketing.campaigns`
- `GET /api/whatsapp/status` (`web.signin`) → `{ "enabled", "provider", "phoneNumberIdHint", "webhookReady",
  "messagesPerMinute" }`. `provider` is `meta` (the Meta Cloud API, `WhatsApp:Enabled`) or `recording` (nothing leaves
  the server; messages are recorded as sent).
- Templates: `GET /api/whatsapp/templates` → `[{ "id", "version", "name", "language", "category", "body",
  "isActive", "placeholders" }]`; `POST` (201), `PUT /{id}`, `DELETE /{id}` (400 while a message uses it). Body
  `{ "name", "language", "category", "body", "isActive", "version" (PUT) }`. `name` is the template's name at WhatsApp
  (lower-case letters, digits and `_`), `language` its code (`en`, `en_US`, `ur`), `category` `Marketing` or `Utility`.
  The body's placeholders become the template's parameters in order: `{FirstName}`, `{LastName}`, `{Name}`, `{Points}`,
  `{Balance}`, `{Store}`, `{ClientNumber}`; any other is 400.
- `CampaignRow`: `{ "id", "version", "name", "segmentId", "segment", "templateId", "template", "status", "storeId",
  "storeCode", "scheduledAtUtc", "startedAtUtc", "finishedAtUtc", "recipients", "counts": { "queued", "sent",
  "delivered", "read", "failed", "error" }, "createdBy", "createdAtUtc" }`. `status` is `Draft`, `Scheduled`, `Sending`,
  `Sent` or `Cancelled`.
- `GET /api/campaigns?storeId&status` (*paged*, newest first); `GET /api/campaigns/{id}`; `POST` (201, a draft),
  `PUT /{id}` (drafts only), `DELETE /{id}` (drafts, and cancelled campaigns that sent nothing). Body `{ "name",
  "segmentId", "templateId", "storeId"?, "version" (PUT) }`.
- `POST /api/campaigns/{id}/schedule` takes `{ "sendAtUtc"?, "version" }` (null: now). The recipients are worked out
  there and then: the segment's members who agreed to WhatsApp messages and have a number, one message each, queued.
  400 when there is nobody. The server's sender sends them from `sendAtUtc` on, at most `WhatsApp:MessagesPerMinute`.
- `POST /api/campaigns/{id}/cancel` takes `{ "version" }`; queued messages are never sent.
- `GET /api/campaigns/{id}/messages?status` (*paged*) → `WhatsAppMessageRow { "id", "campaignId", "clientId",
  "clientName", "toNumber", "template", "body", "status", "error", "sentAtUtc", "deliveredAtUtc", "readAtUtc",
  "createdAtUtc" }`. `status` is `Queued`, `Sent`, `Delivered`, `Read`, `Failed` (WhatsApp could not deliver it) or
  `Error` (it was not sent: refused, no consent any more, or the API failed).
- `GET /api/campaigns/{id}/results?days=7` → `{ "counts", "recipients", "days", "buyers", "sales", "salesTotal" }`: the
  recipients' completed sales from the campaign's start to `days` days after (1–90).
- Single messages: `POST /api/clients/{id}/whatsapp` (`crm.edit`) takes `{ "templateId", "storeId"? }` and sends at
  once → `WhatsAppMessageRow` (400 without consent or number). `GET /api/clients/{id}/whatsapp` (`crm.view`) lists the
  client's messages, newest first.
- Webhook (no sign-in): `GET /api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=…&hub.challenge=…` answers the
  challenge when the token is `WhatsApp:VerifyToken`, else 403. `POST /api/whatsapp/webhook` takes Meta's status
  notifications; `X-Hub-Signature-256` must be `sha256=` and the HMAC-SHA256 of the body with `WhatsApp:AppSecret`
  (403 otherwise). Statuses only move forward (sent, delivered, read); `failed` keeps the error. → 200.

### Clients (additions)
`ClientDetail` gains `whatsAppNumber`, `whatsAppOptIn`, `whatsAppOptInAtUtc` (read-only: when consent was given) and
`birthday` (`yyyy-MM-dd`). Leaving them out of a PUT keeps them. Consent can be given only with a number; taking it
away clears the time. `ClientListRow` gains `whatsAppOptIn`.

### Promotions — `marketing.promotions`
- `PromotionTarget`: `{ "departmentId"?, "categoryId"?, "plu"?, "quantity", "name" }` (one of the three; `name` is
  display text the server fills in).
- `PromotionRow`: `{ "id", "version", "name", "kind", "isActive", "startsOn", "endsOn", "priority", "combinable",
  "allStores", "storeIds", "couponCode", "couponMaxUses", "couponUses", "summary", "live" }`. `kind` is `PercentOff`,
  `AmountOff`, `BuyXGetY`, `BundlePrice` or `SpendOver`; `live` is true when it is on and today is within its dates.
- `PromotionDetail` adds `{ "description", "days", "startTime", "endTime", "scope", "targets", "percent", "amount",
  "buyQuantity", "getQuantity", "getPercent", "rewardTargets", "bundlePrice", "bundleParts", "spendThreshold" }`.
  - `days`: the days of the week it runs (0 Sunday … 6 Saturday; empty every day), `startTime`/`endTime` `HH:mm` (a
    happy hour; an end before the start runs past midnight).
  - `scope` (`AllItems`, `Departments`, `Categories`, `Items`) and `targets`: what it applies to (what must be bought
    for `BuyXGetY`; what is counted for `SpendOver`).
  - `PercentOff`: `percent` off each line; `AmountOff`: `amount` off each unit; `BuyXGetY`: buy `buyQuantity`, get
    `getQuantity` at `getPercent` off (100 = free) of `rewardTargets` (empty: the same items, the cheapest free);
    `BundlePrice`: `bundleParts` (with quantities) together for `bundlePrice`; `SpendOver`: when the counted lines reach
    `spendThreshold`, `percent` (or else `amount`) off, as a line of its own.
  - `couponCode`: applies only when the code is entered at the till; `couponMaxUses` 0 means no limit, 1 one use.
  - `combinable` false: used alone on a sale. The till picks the best price for the customer among the promotions
    used alone and all the combinable ones together; `priority` (higher first) breaks ties. A line takes one
    promotion; lines with a price or discount changed by hand take none.
- `GET /api/promotions?search&active` → `PromotionRow[]`; `GET /api/promotions/{id}` → `PromotionDetail`; `POST` (201),
  `PUT /{id}`, `DELETE /{id}` (400 once it was used: switch it off instead). Body: `PromotionDetail` without the
  read-only fields.
- `GET /api/promotions/report?storeId&from&to` → `[{ "promotionId", "name", "kind", "sales", "units", "netSales",
  "discount", "couponUses" }]`: what each promotion cost (`discount`) and earned (`netSales` of its lines, or of the
  sales it applied to for `SpendOver`) on completed sales.

### Sales targets — `sales.targets`
- `SalesTargetRow`: `{ "id", "version", "storeId", "storeCode", "userId", "userName", "year", "month", "amount",
  "actual", "percent" }`. `userId` null is the store's target; `actual` is net sales (as the dashboard counts them) of
  that store, or of that salesperson at that store, in the month.
- `GET /api/targets?storeId&year&month` (default this month) → `SalesTargetRow[]`, store targets first.
- `POST /api/targets` takes `{ "storeId", "userId"?, "year", "month", "amount" }` → 201 (400 when that target exists);
  `PUT /api/targets/{id}` takes `{ "amount", "version" }`; `DELETE /api/targets/{id}`.
- `GET /api/targets/progress?storeId` (`sales.targets` or `dashboard.view`) → `{ "year", "month", "day",
  "daysInMonth", "target", "actual", "percent", "stores": [{ "storeId", "storeCode", "target", "actual", "percent" }],
  "people": [{ "userId", "userName", "storeCode", "target", "actual", "percent" }] }` for this month: the dashboard tile.

### Web pages (E4)

| Page | Path | Permission |
|---|---|---|
| CRM | `/crm/` | `crm.view`; tabs Pipeline (board), Leads, Activities, Won and lost |
| My tasks | `/tasks/` | `crm.view` |
| Segments | `/segments/` | `marketing.campaigns` |
| Campaigns | `/campaigns/` | `marketing.campaigns`; tabs Campaigns and Templates |
| Campaign | `/campaigns/view/?id=` | `marketing.campaigns`; recipients and results |
| Promotions | `/promotions/` | `marketing.promotions`; list, editor, report |
| Targets | `/targets/` | `sales.targets` |

The Dashboard adds two sections: the signed-in user's overdue and today's activities (with `crm.view`) and this month's
targets against sales (with `sales.targets`). The client editor gains the WhatsApp number, consent and *Send on
WhatsApp*.
