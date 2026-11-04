namespace RetailPlus.App.Help;

/// <summary>A part of a help topic: a heading with paragraphs, numbered steps and term-and-meaning pairs.</summary>
public sealed record HelpSection(string Heading, IReadOnlyList<string>? Paragraphs = null, IReadOnlyList<string>? Steps = null,
    IReadOnlyList<(string Term, string Meaning)>? Definitions = null);

/// <summary>
/// A help topic. <see cref="Screen"/> is the screen it explains (a key of MainWindow's list), so the topic can open it
/// and the screen's help can find it.
/// </summary>
public sealed record HelpTopic(string Key, string Title, string Summary, string? Screen, IReadOnlyList<HelpSection> Sections, string Keywords = "")
{
    /// <summary>Everything a search looks through.</summary>
    public string SearchText => string.Join(' ', new[] { Title, Summary, Keywords }
        .Concat(Sections.SelectMany(s => new[] { s.Heading }
            .Concat(s.Paragraphs ?? []).Concat(s.Steps ?? []).Concat((s.Definitions ?? []).SelectMany(d => new[] { d.Term, d.Meaning })))));
}

/// <summary>
/// The help of the desktop app. Written for someone at the till in a hurry: short steps, the words on the buttons and the
/// keys printed on them. It says how to do things, not why the rules are what they are.
/// </summary>
public static class HelpTopics
{
    public static IReadOnlyList<HelpTopic> All { get; } =
    [
        new("start", "Signing in and finding your way", "Logging in, the screens on the left, and the keys that take you anywhere.", "home",
        [
            new("Signing in",
                Steps:
                [
                    "Type your user name and password, then press Enter.",
                    "Or press F4 to use your staff code and PIN instead: the quick sign-in at the till.",
                    "The first time, or after a manager reset your password, you are asked to choose a new password. Type it twice.",
                ]),
            new("If you cannot sign in",
                Paragraphs:
                [
                    "Five wrong passwords or PINs in a row lock the account for five minutes. The window says how many minutes are left.",
                    "A manager can unlock the account at once with Setup › Users › Reset password. A user who is not active, or who works at another store, cannot sign in here.",
                ]),
            new("Finding your way",
                Paragraphs:
                [
                    "The list on the left holds the screens your role may use. The key next to each name opens it: Ctrl+1 for the dashboard, Ctrl+2 for the Point of Sale, and so on.",
                    "Press Ctrl+K anywhere and type a few letters (\"inv\", \"end of day\", \"hours\") to go straight to a screen, a report, an action or a help topic.",
                    "Every button shows its key on the right. Ctrl+Shift+K lists the keys; Ctrl+Shift+H opens this help.",
                    "Switch user (Ctrl+Shift+U) at the top right lets the next person sign in without closing the program.",
                ]),
        ], "login log in password pin staff code locked lockout switch user ctrl+k"),

        new("pos", "Point of Sale", "Ringing up a sale at the till.", "pos",
        [
            new("Selling something",
                Steps:
                [
                    "Scan the barcode, or type the item's code and press Enter. Whatever is scanned goes into the code box, wherever you clicked last.",
                    "Scan the same item again for a second one: it joins the line, which then shows 2.",
                    "To sell several at once, type 3*CODE (three of that item).",
                    "When everything is on the list, press F12 (Pay) or Enter on an empty code box.",
                ]),
            new("The keys on the right",
                Definitions:
                [
                    ("Ctrl+F Find item", "Search the inventory by name."),
                    ("Ctrl+M Item without code", "Sell something that is not in the inventory, at a price you type."),
                    ("Ctrl+J Coupon", "Type a coupon code for a promotion (or scan it into the code box). Press it again with the same code to take the coupon off."),
                    ("F4 Choose client", "Put a client on the sale, for their prices, points and account. Shift+F4 removes them."),
                    ("F5 Change quantity", "Change how many of the chosen line."),
                    ("F6 Change price, F7 Discount on line, F8 Discount on sale", "May need a supervisor; see When a supervisor is needed."),
                    ("Del Remove line", "Take the chosen line off the sale."),
                    ("F9 Return / refund", "The next item is taken back instead of sold."),
                    ("F11 Use points", "Turn the client's bonus points into a discount."),
                    ("Ctrl+H Put on hold, Ctrl+B Bring back", "Park a sale while you serve someone else, then recall it at any till."),
                    ("Ctrl+Q Save as quote, Ctrl+L Layaway", "Keep the sale as a price quote, or as a layaway with a deposit."),
                    ("Esc Cancel sale", "Clear the whole sale. Nothing is saved."),
                ]),
            new("Promotions and coupons",
                Paragraphs:
                [
                    "Promotions are set on the web back office and reach this till with the sync. They apply by themselves: the line shows the promotion's name in green, the Discount column what it takes off, and the panel on the right what the promotions save in all. The receipt shows them too.",
                    "A promotion with a coupon code applies only after the code is scanned or typed: in the code box, with Coupon (Ctrl+J), or with Coupon in the Pay window. A code that is not known, used up or out of date is refused in red.",
                    "A line whose price or discount you changed by hand takes no promotion. When several promotions could apply, the customer gets the best price.",
                ]),
            new("WhatsApp messages",
                Paragraphs:
                [
                    "The first time a client is chosen for a sale, the till asks you to put a question to them: may we send you our offers and news on WhatsApp? Choose Yes with their number (with + and the country code), No, or Ask later.",
                    "Yes and No are kept on the client, so they are not asked again. Messages are only ever sent to clients who said yes. To change the answer later, open the client on the Clients screen.",
                ]),
            new("Training mode",
                Paragraphs: ["Users with the Trainee role see TRAINING MODE: their sales are rung up and printed but never saved, and stock does not change."]),
        ], "sell sale scan barcode quantity price discount return refund hold suspend recall quote layaway cancel training promotion coupon whatsapp consent offers"),

        new("paying", "Paying and receipts", "Taking the money, change, split payments and printing the receipt.", "pos",
        [
            new("Taking payment",
                Steps:
                [
                    "Press F12 (Pay). The total is shown at the top.",
                    "Type the amount the customer hands over, then press the key of the payment type (F4 … F12). Enter means cash.",
                    "If they pay part by card and part in cash, take one payment, then the next: the window shows what is still due.",
                    "The change to give back is shown in large figures. The drawer opens for cash.",
                ]),
            new("Receipts",
                Paragraphs:
                [
                    "The receipt prints after each sale when the station has a receipt printer (Setup › This station). Ctrl+P (Print last receipt) prints it again.",
                    "Older receipts are in the Sales Journal: choose the sale and View receipt.",
                ]),
        ], "pay payment tender cash card change split receipt print"),

        new("drawer", "Cash drawer and End of Day", "Starting cash, money in and out, the drawer report and closing the day.", "pos",
        [
            new("During the day",
                Definitions:
                [
                    ("Ctrl+T Starting cash", "The float put in the drawer at the start."),
                    ("Ctrl+I Money in, Ctrl+O Money out", "Cash added or taken out for a reason (change from the bank, paying a delivery)."),
                    ("Ctrl+D Open drawer", "Opens the drawer without a sale. You give a reason, and it is written in the audit log."),
                    ("Ctrl+R Drawer report", "The X report: what the drawer should hold now. Nothing is closed."),
                ]),
            new("End of Day",
                Steps:
                [
                    "Count the drawer.",
                    "Press Ctrl+E (End of day) and confirm. It may need a supervisor.",
                    "The Z report prints: sales, taxes, each payment type and the cash expected. Compare it with your count.",
                    "The next sale starts a new day for this station.",
                ]),
        ], "float pay in pay out no sale x report z report end of day close drawer"),

        new("approvals", "When a supervisor is needed", "Actions your role may not do alone, and how a supervisor approves them at your till.", null,
        [
            new("What needs a supervisor",
                Paragraphs:
                [
                    "What you may do comes from your role. When you try something your role does not allow (a discount, a void, a return, End of Day, opening the drawer…) an Approval needed window opens instead of a refusal.",
                    "Removing a line or cancelling a sale needs a supervisor only when the store asks for it (Setup › POS & security).",
                ]),
            new("Approving",
                Steps:
                [
                    "The supervisor types their name and password, or presses F4 and types their staff code and PIN.",
                    "They are accepted when their own role may approve and may do the action themselves.",
                    "You stay signed in. The approval, or the refusal, is written in the audit log with both names.",
                ]),
        ], "supervisor approval approve permission override void discount"),

        new("journal", "Sales Journal", "Looking up past sales, reprinting a receipt and voiding a sale.", "journal",
        [
            new("Finding a sale",
                Paragraphs: ["Choose the dates and press Show. Tick This station only to see this till's sales. Double-click a sale to see its receipt."]),
            new("Voiding a sale",
                Steps:
                [
                    "Choose the sale and press Void sale. This needs the right to void, or a supervisor.",
                    "Type the reason.",
                    "The stock comes back, the drawer totals are corrected and any invoice on the client's account is cancelled. The voided sale stays in the journal, marked Voided, with who voided it.",
                ]),
        ], "journal history past sales reprint void"),

        new("inventory", "Inventory: items and prices", "Adding and changing the things you sell.", "inventory",
        [
            new("Adding an item",
                Steps:
                [
                    "Press Ctrl+N (New item).",
                    "Type the stock code, the description and Price 1. The other tabs hold costs, suppliers, matrix sizes, kits and serial numbers.",
                    "Leave the barcode empty to have the shop's own UPC-A barcode made when you print labels, or scan the maker's barcode into it.",
                    "Save. The item can be sold at once.",
                ]),
            new("Other buttons",
                Definitions:
                [
                    ("Ctrl+O Edit, Ctrl+D Duplicate", "Change an item, or start a new one from a copy."),
                    ("Del Delete, Ctrl+U Undelete", "Deleted items go to the trash; tick Show deleted to bring them back."),
                    ("Ctrl+R Change prices", "Raise or lower Price 1 by a percentage, or set it from cost and margin, for flagged items, a department or all."),
                    ("Ctrl+G Flag / unflag", "Mark items for a purchase order, labels or a price change."),
                    ("Ctrl+E Export CSV, Ctrl+I Import CSV", "Exchange items with a spreadsheet; Ctrl+T saves a template with every column."),
                ]),
            new("Who changed it",
                Paragraphs: ["Every change to an item, with the old and new values, is in the audit log (Ctrl+Shift+A)."]),
        ], "item product price cost barcode new edit delete undelete csv export import change prices"),

        new("stock", "Stock", "Adjusting counts and seeing every movement.", "inventory",
        [
            new("Adjusting stock",
                Steps:
                [
                    "Choose the item and press Ctrl+J (Adjust stock).",
                    "Add to (or take from) the count, or set the counted quantity after a stock take.",
                    "Give a reason. The change is kept as a stock movement with your name.",
                ]),
            new("Where the count comes from",
                Paragraphs: ["Every sale, return, void, receipt from a supplier, adjustment and CSV import writes a stock movement. The Stock adjustments report lists the adjustments for a period."]),
        ], "stock on hand count adjust stock take movement"),

        new("labels", "Barcodes and labels", "Printing barcode labels on A4 sheets or a label printer.", "inventory",
        [
            new("Printing labels",
                Steps:
                [
                    "In Inventory, choose the items (or none for all items shown) and press Ctrl+P (Print labels).",
                    "Choose the sheet: A4 sheets of 30 or 24, or a label printer roll.",
                    "Print, or save the labels as a PDF.",
                ]),
            new("Barcodes",
                Paragraphs: ["Items without the maker's barcode get the shop's own UPC-A barcode. A USB scanner works like a keyboard, so it scans into the POS wherever you clicked."]),
        ], "barcode label print upc ean sticker pdf"),

        new("rfid", "RFID tags and readers", "Tagged items that ring up by themselves.", "rfid",
        [
            new("At the till",
                Paragraphs:
                [
                    "When the counter's antenna reads a tagged item it is added to the sale by itself, with a beep. Tagged pieces already sold are not added again.",
                    "The RFID pill on the POS pauses and resumes reading (Ctrl+Shift+P). The chip at the top of the window says whether the readers are connected.",
                ]),
            new("Setting up readers",
                Paragraphs: ["RFID readers lists the readers, their four antennas and the station each antenna serves. Find readers searches the network. A site with many readers runs the RFID hub on one PC, and every station points at it in Setup › This station."]),
        ], "rfid tag epc reader antenna hub"),

        new("clients", "Clients", "Customer records, bonus points and accounts.", "clients",
        [
            new("Working with clients",
                Paragraphs:
                [
                    "Search by name, number or phone. Ctrl+N adds a client; Ctrl+O changes one.",
                    "Sell to client opens the Point of Sale with the client already on the sale. Statement prints what they owe.",
                    "A client's balance comes from on-account sales and layaways, less their payments.",
                ]),
        ], "client customer account balance points statement"),

        new("invoices", "Invoices & A/R", "On-account invoices, quotes and layaways, and the payments on them.", "invoices",
        [
            new("Taking a payment",
                Steps:
                [
                    "Choose the invoice and press F12 (Take payment).",
                    "Choose how they pay and type the amount. Late charges are paid first.",
                    "Distribute payment spreads one payment over a client's open invoices, oldest first.",
                ]),
            new("Quotes and layaways",
                Paragraphs: ["Sell quote / layaway loads a quote back into the Point of Sale. Layaway payments are taken with Take payment."]),
        ], "invoice receivable account payment quote layaway late charge statement"),

        new("purchasing", "Suppliers and purchase orders", "Ordering from suppliers and receiving what arrives.", "po",
        [
            new("Ordering",
                Steps:
                [
                    "Press New order and choose how many to order: blank, one or two weeks' sales, reorder points or last year's month.",
                    "Change quantities and costs in the list, then Save changes.",
                    "Post order adds the quantities to On Order. Print sends it to the supplier.",
                ]),
            new("Receiving",
                Steps:
                [
                    "Type the pieces received in the Received column, or press Receive all.",
                    "Press Post shipment and give any freight cost. Stock and costs are updated, and the order shows who received it.",
                ]),
        ], "supplier purchase order po receive shipment reorder"),

        new("orders", "Customer orders and RMAs", "Things promised to customers, and returns to suppliers.", "orders",
        [
            new("Customer orders",
                Paragraphs: ["New customer order records an item a client is waiting for; Mark paid / unpaid shows whether it is paid for. Open only hides the paid ones."]),
            new("RMAs",
                Paragraphs: ["New RMA records goods going back to a supplier (and the customer who returned them, if any). Close / reopen RMA when the supplier has dealt with it."]),
        ], "customer order back order rma return supplier"),

        new("reports", "Reports", "Figures for the period you choose, printed or saved.", "reports",
        [
            new("Running a report",
                Steps:
                [
                    "Choose the report on the left.",
                    "Set From and To, or press Today, This month or This year.",
                    "Press F5 (Run report). Print it, or save it as CSV for a spreadsheet or as HTML.",
                ]),
            new("Staff reports",
                Paragraphs: ["Hours worked (by staff member, day or week) and Commission by staff member need the staff reports permission."]),
        ], "report daily sales department item tax voids stock valuation aging print csv"),

        new("timeclock", "Time clock and hours", "Punching in and out, correcting a shift, and the hours reports.", "staff",
        [
            new("Punching in and out",
                Steps:
                [
                    "At the log in window, type your name and password (or staff code and PIN) and press Ctrl+T (Punch in / out).",
                    "Or open Staff (Ctrl+Shift+E) and press Punch in / out for yourself.",
                    "You can be punched in only once at a time. Punching again punches you out.",
                ]),
            new("Correcting a shift",
                Paragraphs:
                [
                    "In Staff › Time clock, a supervisor chooses the entry and presses Change entry, or Add entry for a shift that was never punched. A reason is needed: it stays on the entry and the change goes to the audit log.",
                    "Users who may not change entries are asked for a supervisor's approval.",
                ]),
            new("Hours",
                Paragraphs: ["Reports › Hours worked counts finished shifts. A shift that runs past midnight counts on both days."]),
        ], "time clock punch in out shift hours timesheet attendance"),

        new("commission", "Commission", "Commission rules, and what each salesperson earned.", "staff",
        [
            new("How commission is worked out",
                Paragraphs:
                [
                    "Each sales line earns commission by the commission rule that fits it best: a rule for the item beats one for its category, which beats one for its department, which beats one for all items. A rule for the salesperson beats one for everyone, and among the rest the one that started most recently wins.",
                    "A line no rule covers keeps the commission it was sold with: the item's own commission percent.",
                ]),
            new("Rules",
                Paragraphs: ["Staff › Commission rules: give the rule a name, choose who it is for and what it covers, a percent of net sales or an amount per unit, and the first and last day."]),
            new("Seeing it",
                Paragraphs: ["Staff › Commission shows each salesperson's sales and commission for the dates chosen, and the lines behind it. Reports › Commission by staff member prints it."]),
        ], "commission salesperson rule percent per unit"),

        new("users", "Users, roles and permissions", "Who may sign in, and what each role may do.", "setup",
        [
            new("Users",
                Paragraphs:
                [
                    "Setup › Users: Add user, then give the name, role, home store and a password. A staff code and PIN allow the quick sign-in at the till.",
                    "Reset password gives a temporary password and unlocks the account; the user chooses a new one at the next sign-in. A user who has left is made not active rather than deleted.",
                ]),
            new("Roles",
                Paragraphs:
                [
                    "Setup › Roles lists what each role may do, grouped as Sales, Stock, Customers, Staff, Reports and Back office. Tick the permissions and press Save role.",
                    "The standard roles (Trainee, Cashier, Stock clerk, Supervisor, Manager, Administrator, Super administrator) can be changed but not renamed or deleted. New role adds your own.",
                    "Nobody can remove the last active user who may manage users and roles.",
                ]),
        ], "user role permission password pin staff code access level administrator manager supervisor cashier"),

        new("audit", "Audit log", "Who changed what, and when.", "audit",
        [
            new("What is in it",
                Paragraphs:
                [
                    "Changes to items and prices, clients, suppliers, users and roles, store settings, payment types, taxes and stations, with the values before and after.",
                    "Sign-ins and failed sign-ins, approvals, voids, opening the drawer without a sale, End of Day, backups, restores, imports and time clock changes.",
                ]),
            new("Finding something",
                Paragraphs: ["Choose the dates, and if you like a user, an action or a word, then press Show. Choose a line to see what changed."]),
        ], "audit log history who changed before after"),

        new("setup", "Setup", "The store's settings, and this station's own.", "setup",
        [
            new("The tabs",
                Definitions:
                [
                    ("Business, Taxes, POS & security, Options", "The store's name and address, tax rates, how strict the till is, bonus points and late charges."),
                    ("Users, Roles", "Who may sign in and what they may do."),
                    ("Payments, Lists, Texts, Fast keys", "Payment types, departments and categories, receipt header and footer, and the quick keys on the POS."),
                    ("This station", "This computer's database, station number, printer, drawer and RFID."),
                ]),
            new("Saving",
                Paragraphs: ["Save settings saves the store's tabs and this station's. Users and roles are saved with their own buttons."]),
        ], "setup settings tax printer station fast keys texts payments"),

        new("tools", "Backup, restore, import and year end", "Looking after the database.", "tools",
        [
            new("Backup and restore",
                Paragraphs:
                [
                    "Back up now writes a full copy of the database to the backup folder. Do it every day, and keep a copy away from the shop.",
                    "Restore replaces the database with a backup: everything entered since is lost. Every station must be closed first.",
                ]),
            new("Other tools",
                Definitions:
                [
                    ("Import from Retail Plus 5", "Brings in the old program's data, once, when the shop moves over."),
                    ("Rebuild", "Checks the database and rebuilds its indexes."),
                    ("Recurring billing", "Makes the invoices of clients billed every month."),
                    ("Year end", "Keeps each item's count as last year's closing stock."),
                ]),
        ], "backup restore rebuild import year end recurring billing database"),

        new("keys", "Keyboard keys", "Everything can be done from the keyboard.", null,
        [
            new("Everywhere",
                Definitions:
                [
                    ("Ctrl+K", "Go to: type to find a screen, report, action or help topic."),
                    ("Ctrl+1 … Ctrl+0, Ctrl+Shift+letter", "Open a screen; the key is shown next to its name."),
                    ("Ctrl+Shift+H", "Help."),
                    ("Ctrl+Shift+K", "The list of keys."),
                    ("Ctrl+Shift+U", "Switch user."),
                    ("Enter, Esc", "OK or save; cancel or close."),
                ]),
            new("Left to Windows",
                Paragraphs: ["F1, F2 and F3 are left to Windows on purpose."]),
        ], "keyboard keys shortcuts hotkeys"),
    ];

    /// <summary>
    /// The topics that match a search, best first: every word must appear somewhere in the topic; a word in the title
    /// counts most.
    /// </summary>
    public static IReadOnlyList<HelpTopic> Search(string? text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return All;
        return All
            .Select(t => (Topic: t, Score: Score(t, words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Topic)
            .ToList();
    }

    private static int Score(HelpTopic topic, string[] words)
    {
        int score = 0;
        foreach (var word in words)
        {
            if (topic.Title.Contains(word, StringComparison.CurrentCultureIgnoreCase)) score += 10;
            else if (topic.Keywords.Contains(word, StringComparison.CurrentCultureIgnoreCase) || topic.Summary.Contains(word, StringComparison.CurrentCultureIgnoreCase)) score += 5;
            else if (topic.SearchText.Contains(word, StringComparison.CurrentCultureIgnoreCase)) score += 1;
            else return 0;
        }
        return score;
    }

    /// <summary>The topic that explains a screen.</summary>
    public static HelpTopic? ForScreen(string screen) => All.FirstOrDefault(t => t.Screen == screen);
}
