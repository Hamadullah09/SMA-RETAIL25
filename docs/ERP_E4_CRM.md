# ERP phase E4: sales, CRM and marketing

Phase E4 of the [ERP plan](ERP_PLAN.md) (section 3.4 and 4): leads and the pipeline, activities and follow-ups,
customer segments, WhatsApp campaigns with consent, promotions and coupons at the till, and sales targets. Built on
branch `erp-e4-crm`, 2026-11-04. The API is in [API.md](API.md), section "New in phase E4".

## What is there

| Area | Where | What it does |
|---|---|---|
| Leads | Web, CRM › Leads | A lead has a name, company, phone, WhatsApp number, e-mail, source, owner and store. *Convert* makes it a client (a new one numbered from the server's range, or one that exists); its opportunities and activities move to the client and keep the lead |
| Pipeline | Web, CRM › Pipeline | Stages with a probability and an outcome (Open, Won, Lost); the company's, New / Qualified / Proposal / Won / Lost to start with. Opportunities are cards on a board, moved between stages; Lost needs a reason. The value is weighted by the probability |
| Won and lost | Web, CRM › Won and lost | What closed in a period: count and value won and lost, the win rate, per owner, and the reasons for losing |
| Activities | Web, CRM › Activities; My tasks; the dashboard | Calls, visits, meetings, notes and tasks on a client, lead or opportunity, with a due time in the store's time. *My tasks* and the dashboard show the user's overdue and today's |
| Segments | Web, Segments | Clients chosen by rules: spend and visits in a period, last purchase (lapsed or recent), a store, a department bought, birthday month, points, balance owing, WhatsApp consent. Preview, the count when last evaluated, CSV of the members |
| WhatsApp | Web, Campaigns; a client's page | Templates as WhatsApp approves them (`{FirstName}`, `{Points}`… become numbered parameters). A campaign sends one template to a segment, now or later; the recipients are the members who agreed and have a number. Counts per status (queued, sent, delivered, read, failed, refused) and the recipients' sales afterwards. One message to one client from their page |
| Consent | The till, the desktop client editor, the web client page | A client's WhatsApp number (international form) and whether they agree, with the time. The till asks a client once (see below). Nothing is ever sent without consent, which is checked again just before each message goes |
| Promotions | Web, Promotions; every till | Percent or amount off, buy X get Y (free or at a discount), bundle price, spend over an amount; for all items, departments, categories or stock codes; dates, days of the week and a happy hour; every store or some; a priority; whether it combines; an optional coupon code with a limit of uses. A report of what each promotion cost and earned |
| Targets | Web, Targets; the dashboard | A monthly sales target per store and per salesperson, against net sales as the dashboard counts them |

### At the till

- **Promotions apply by themselves.** The Point of Sale screen reads the promotions that may run at its store (they
  come down with the sync) and works them out again after every change to the sale, with `PromotionEngine` in
  `RetailPlus.Core`: the same rules the server uses for its report and the web for its previews. The customer gets the
  best price the combine rules allow; a line takes one promotion; a line whose price or discount was changed by hand
  takes none; returns, gift cards and text lines never do. A line shows its promotion's name in green under the item and
  the discount in the Discount column; a sale-wide promotion (spend over an amount) is a line of its own; the totals
  panel says what the promotions save.
- **Coupons**: scanned or typed into the code box (a code that is no item's), with *Coupon* (Ctrl+J), or with *Coupon*
  (Ctrl+J) in the Pay window, which then opens again with the new total. An unknown, used-up or out-of-date code is
  refused in red; a code whose promotion the sale does not qualify for yet stays on the sale until it does. Entering a
  code that is already on the sale offers to take it off; removing the coupon's line (Del) takes it off too. A coupon's uses are
  counted across every till the till has heard from; the sale is refused if the last use went at another till in the
  meantime, and the total is worked out again.
- **The receipt** names the promotion under each line with what it took off, shows sale-wide promotions as lines, and
  ends with "You saved … with promotions". Each sale line keeps its promotion and its discount (`SaleLine.PromotionId`,
  `PromotionName`, `PromotionDiscount`), and the coupons a sale used are `CouponUse` rows; a void gives them back.
- **WhatsApp consent.** When a client who never answered is chosen for a sale, a window asks the cashier to put the
  question: may we send you our offers and news on WhatsApp? *Yes, they agree* (Enter) needs the number with + and the
  country code; *No* (Ctrl+N) is kept too; *Ask later* (Esc) records nothing. The answer and its time
  (`Client.WhatsAppAskedAt`, `WhatsAppOptIn`, `WhatsAppOptInAt`) are saved on the client and audited ("WhatsApp
  consent"), without the right to change clients, and go to the server with the next sync. The client line at the POS
  shows "WhatsApp offers: yes" or "no". The desktop client editor shows and changes the number and consent, with when
  they agreed; giving or taking back consent there counts as an answer. A trainee is never asked.
- The keys stay the desktop's: every new button shows its key, F1–F3 are left to Windows, and the help (Ctrl+Shift+H,
  *Selling*) and the key list (Ctrl+Shift+K) describe promotions, coupons and the WhatsApp question.

### In the books

Promotions reach the general ledger through the existing daily posting (`LedgerService.PostDailyAsync`, which posts
`AccountingService`'s daily journal), not a second path. A department's sales are credited before the promotions, and
what they took off (a line's promotion discount, and the line of a sale-wide promotion) is debited to the store's
*Discounts* account (standard account 4020) with the memo "Promotions". The entry still balances, and the totals taken
are unchanged.

### Sending WhatsApp messages

Sending is behind `IWhatsAppSender` (`RetailPlus.Data/Services/WhatsAppSender.cs`):

- `MetaCloudWhatsAppSender` (server) posts template messages to the WhatsApp Business Cloud API. It is used only when
  `WhatsApp:Enabled` is true and `WhatsApp:PhoneNumberId` and `WhatsApp:AccessToken` are set (user secrets or
  environment variables on the server, never in the repository). `appsettings.json` has it off, with empty values.
- Until then, and in every test, `RecordingWhatsAppSender` keeps what would have been sent and sends nothing.
- A background worker on the server sends due campaigns a few at a time (at most `WhatsApp:MessagesPerMinute`, every
  `WhatsApp:WorkerIntervalSeconds`). `/api/whatsapp/webhook` takes Meta's delivery and read statuses, checking
  `WhatsApp:VerifyToken` and the `X-Hub-Signature-256` signature made with `WhatsApp:AppSecret`.
- Another provider (SMS, e-mail) is another `IWhatsAppSender`-like sender; nothing else needs to change.

## Data, sync and permissions

| Table | Where it lives |
|---|---|
| `Lead`, `OpportunityStage`, `Opportunity`, `CrmActivity`, `CustomerSegment`, `WhatsAppTemplate`, `Campaign`, `WhatsAppMessage`, `SalesTarget` | Server only (`ILocalOnly`) |
| `Promotion`, `PromotionTarget`, `PromotionStore` | Set on the server, sent down to every till (`SyncFlow.Down`); each till uses those of its store |
| `CouponUse` | Written at the till with the sale; goes up and to every till (`Everywhere`, both ways), so each till knows how often a coupon was used |
| `Client` (WhatsApp number, consent, `WhatsAppAskedAt`), `SaleLine` (promotion columns) | As before: clients everywhere both ways, sales up |

- Permissions (`Core.Rules.Permissions`): `crm.view`, `crm.edit`, `marketing.campaigns`, `marketing.promotions`,
  `sales.targets`. Supervisors get `crm.view`; Managers and Administrators get all five; the new **Marketing** role
  works on the web with leads, the pipeline, segments, campaigns and promotions (not targets).
- Store rules: leads, opportunities, activities, campaigns, messages and targets belong to a store; stages, templates
  and segments without a store are the company's; a promotion for one store may be changed by that store's users.
- Audit log: changes to leads, stages, opportunities, targets, segments, templates, campaigns and promotions, plus
  "Lead converted", "Opportunity won/lost", "Campaign scheduled/cancelled/sent", "WhatsApp message" and "WhatsApp
  consent".
- Both `InitialCreate` migrations are made again for the model (last: `Client.WhatsAppAskedAt`).

## Code

| Where | What |
|---|---|
| `src/RetailPlus.Core/Model/Crm.cs`, `Marketing.cs`, `Promotions.cs` | The model |
| `src/RetailPlus.Core/Rules/Promotions.cs`, `Segments.cs`, `WhatsApp.cs` | The promotions engine, segment rules in words, WhatsApp numbers and templates |
| `src/RetailPlus.Data/Services/CrmService.cs`, `SegmentService.cs`, `CampaignService.cs`, `PromotionService.cs`, `TargetService.cs`, `WhatsAppSender.cs`, `PartyService.RecordWhatsAppAnswerAsync` | The services |
| `src/RetailPlus.Server/Endpoints.Crm.cs`, `Endpoints.Marketing.cs`, `Endpoints.Promotions.cs`, `WhatsApp.cs` | The API, the Meta sender, the worker and the webhook |
| `src/RetailPlus.App/Views/PosViewModel.cs` (Promotions and coupons), `Dialogs/WhatsAppConsentWindow.cs`, `Dialogs/PayWindow.cs`, `Dialogs/PartyEditorWindow.xaml` | The till |
| `src/RetailPlus.Reports/Receipt.cs`, `ReceiptFormatter.cs` | Promotions on the receipt |
| `web/src/app/(app)/crm`, `tasks`, `segments`, `campaigns`, `promotions`, `targets`; `web/src/components/crm.tsx`, `campaigns.tsx`, `client-whatsapp.tsx`, `dashboard-extras.tsx` | The web pages |

## Tests

- `tests/RetailPlus.Core.Tests/PromotionTests.cs`, `MarketingRulesTests.cs`: every kind of promotion, combining, the
  best price, coupons, happy hours and dates; segment rules, numbers and templates.
- `tests/RetailPlus.Data.Tests/CrmAndMarketingTests.cs`, on SQLite and on SQL Server: leads to clients, the pipeline,
  my tasks, segments, campaigns only to consenting numbers (with the recording sender), single messages, promotions on
  posted sales (lines, coupons, voids, the report), targets, consent, the till's WhatsApp answer (kept, audited and
  followed by a campaign), and promotions in the daily journal and the ledger. `ReceiptTests`: promotions on the
  receipt.
- `tests/RetailPlus.Server.Tests/CrmApiTests.cs`: every group of endpoints with its permission, store scope and 409 on
  an old version; a promotion that goes down to a till whose coupon use comes back up and reaches the other store's
  till.
- Web: Vitest (`crm`, `promotions`, `segments`, `targets`, `whatsapp`, `nav`) and Playwright specs for every E4 page,
  answered from the fixtures in `web/e2e/fixtures.ts` and `mock-api.ts`.
- The desktop `--smoke` check rings up a sale with 10% off one item, buy 2 get 1 free on another and a one-use coupon
  typed into the code box; it checks the lines, the saved sale, the coupon's use, the receipt, that the used coupon is
  refused next time, and that a client's yes and another's no to WhatsApp are recorded once each.

## Not done yet

- Promotions are set on the web only: a shop that runs its tills without a server has no screen for them on the desktop.
- Only WhatsApp is wired; SMS and e-mail campaigns need a sender of their own.
- The real WhatsApp sender has been tested against a stand-in, never against Meta's API (no account, no keys).
- Returns do not take back the promotion a sold line had: an item returned on its own is refunded at the price the
  cashier rings up.
