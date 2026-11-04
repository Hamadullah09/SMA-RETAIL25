namespace RetailPlus.Core.Rules;

/// <summary>One thing a role may do, shown in the role editor under its group.</summary>
public sealed record PermissionInfo(string Key, string Group, string Description);

/// <summary>A role every database starts with, and the Retail Plus access level its users get.</summary>
public sealed record DefaultRole(string Name, int Level, string Description, IReadOnlyList<string> Permissions);

/// <summary>
/// The permission keys of the desktop app and the web back office, and the roles a new database gets. Keys are stored in
/// RoleGrants, so they never change once released.
/// </summary>
public static class Permissions
{
    public const string PosSell = "pos.sell";
    public const string PosDiscount = "pos.discount";
    public const string PosReturn = "pos.return";
    public const string PosVoidLine = "pos.void-line";
    public const string PosVoidSale = "pos.void-sale";
    public const string PosNoSale = "pos.no-sale";
    public const string PosDrawer = "pos.drawer";
    public const string PosEndOfDay = "pos.end-of-day";
    public const string PosApprove = "pos.approve";

    public const string InventoryView = "inventory.view";
    public const string InventoryEdit = "inventory.edit";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryCount = "inventory.count";
    public const string InventoryTransfer = "inventory.transfer";
    public const string InventoryPrices = "inventory.prices";
    public const string InventoryLabels = "inventory.labels";
    public const string PurchasingManage = "purchasing.manage";

    public const string ClientsView = "clients.view";
    public const string ClientsEdit = "clients.edit";
    public const string SuppliersEdit = "suppliers.edit";
    public const string InvoicesManage = "invoices.manage";
    public const string RmaManage = "rma.manage";
    public const string GiftCardsIssue = "giftcards.issue";

    public const string TimeClockPunch = "timeclock.punch";
    public const string TimeClockEdit = "timeclock.edit";

    public const string DashboardView = "dashboard.view";
    public const string ReportsView = "reports.view";
    public const string ReportsStaff = "reports.staff";

    public const string SetupStore = "setup.store";
    public const string SetupUsers = "setup.users";
    public const string SetupStations = "setup.stations";
    public const string RfidManage = "rfid.manage";
    public const string BackupManage = "backup.manage";
    public const string ImportRun = "import.run";
    public const string YearEndRun = "yearend.run";
    public const string AuditView = "audit.view";
    public const string AccountingExport = "accounting.export";
    public const string SyncSend = "sync.send";

    public const string WebSignIn = "web.signin";
    public const string StoresManage = "stores.manage";
    public const string StoresAll = "stores.all";

    public const string CrmView = "crm.view";
    public const string CrmEdit = "crm.edit";
    public const string MarketingCampaigns = "marketing.campaigns";
    public const string MarketingPromotions = "marketing.promotions";
    public const string SalesTargets = "sales.targets";
    public const string FinanceView = "finance.view";
    public const string FinancePost = "finance.post";
    public const string FinanceApprove = "finance.approve";
    public const string FinanceClose = "finance.close";
    public const string FinanceSetup = "finance.setup";
    public const string FinancePayables = "finance.payables";
    public const string FinanceBank = "finance.bank";
    public const string FinanceBudget = "finance.budget";

    public const string HrView = "hr.view";
    public const string HrEdit = "hr.edit";
    public const string HrAttendance = "hr.attendance";
    public const string HrLeave = "hr.leave";
    public const string PayrollRun = "payroll.run";
    public const string PayrollApprove = "payroll.approve";

    public const string ProductionView = "production.view";
    public const string ProductionPlan = "production.plan";
    public const string ProductionFloor = "production.floor";
    public const string ProductionCosting = "production.costing";

    public static IReadOnlyList<PermissionInfo> All { get; } =
    [
        new(PosSell, "Sales", "Ring up sales"),
        new(PosDiscount, "Sales", "Give discounts and change prices at the POS"),
        new(PosReturn, "Sales", "Take returns"),
        new(PosVoidLine, "Sales", "Remove lines and cancel sales without a supervisor"),
        new(PosVoidSale, "Sales", "Void a completed sale"),
        new(PosNoSale, "Sales", "Open the drawer without a sale"),
        new(PosDrawer, "Sales", "Open and count the drawer, pay in and pay out"),
        new(PosEndOfDay, "Sales", "Run End of Day"),
        new(PosApprove, "Sales", "Approve what needs a supervisor at another user's till"),

        new(InventoryView, "Stock", "See items and stock"),
        new(InventoryEdit, "Stock", "Add, change and delete items"),
        new(InventoryAdjust, "Stock", "Adjust stock"),
        new(InventoryCount, "Stock", "Count stock and post counts"),
        new(InventoryTransfer, "Stock", "Send and receive transfers between stores"),
        new(InventoryPrices, "Stock", "Change prices in bulk"),
        new(InventoryLabels, "Stock", "Print labels"),
        new(PurchasingManage, "Stock", "Purchase orders and receiving"),

        new(ClientsView, "Customers", "See clients"),
        new(ClientsEdit, "Customers", "Add and change clients"),
        new(SuppliersEdit, "Customers", "Add and change suppliers"),
        new(InvoicesManage, "Customers", "Invoices, quotes, layaways and their payments"),
        new(RmaManage, "Customers", "Returns to suppliers and from customers (RMA)"),
        new(GiftCardsIssue, "Customers", "Sell and reload gift cards"),

        new(TimeClockPunch, "Staff", "Punch in and out"),
        new(TimeClockEdit, "Staff", "Change time clock entries and commission rules"),

        new(DashboardView, "Reports", "See the dashboard"),
        new(ReportsView, "Reports", "Run reports"),
        new(ReportsStaff, "Reports", "Staff hours and commission reports"),

        new(SetupStore, "Back office", "Business, taxes, POS settings, tenders, lists and texts"),
        new(SetupUsers, "Back office", "Users and roles"),
        new(SetupStations, "Back office", "Stations and this station's settings"),
        new(RfidManage, "Back office", "RFID readers and tags"),
        new(BackupManage, "Back office", "Back up, restore and rebuild the database"),
        new(ImportRun, "Back office", "Import Retail Plus data and CSV files"),
        new(YearEndRun, "Back office", "Year end"),
        new(AuditView, "Back office", "See the audit log"),
        new(AccountingExport, "Back office", "Export to accounting"),
        new(SyncSend, "Back office", "Send this till's data to the server"),

        new(WebSignIn, "Web", "Sign in to the web back office"),
        new(StoresManage, "Web", "Add stores and tills"),
        new(StoresAll, "Web", "See and manage every store (super admin)"),

        new(CrmView, "CRM and marketing", "See leads, the pipeline and activities"),
        new(CrmEdit, "CRM and marketing", "Add and change leads, opportunities and activities; send a WhatsApp message"),
        new(MarketingCampaigns, "CRM and marketing", "Customer segments, WhatsApp templates and campaigns"),
        new(MarketingPromotions, "CRM and marketing", "Promotions and coupons at the till"),
        new(SalesTargets, "CRM and marketing", "Sales targets per store and salesperson"),
        new(FinanceView, "Finance", "See the ledger, statements and balances"),
        new(FinancePost, "Finance", "Enter journal entries, bills, expenses and payments"),
        new(FinanceApprove, "Finance", "Approve journal entries made by hand"),
        new(FinanceClose, "Finance", "Lock periods and close the financial year"),
        new(FinanceSetup, "Finance", "Chart of accounts, account mapping and ledger settings"),
        new(FinancePayables, "Finance", "Supplier bills, debit notes and payments"),
        new(FinanceBank, "Finance", "Bank and cash accounts, transfers, deposits and reconciliation"),
        new(FinanceBudget, "Finance", "Budgets"),

        new(HrView, "HR", "See employees, shifts, attendance and leave"),
        new(HrEdit, "HR", "Add and change employees, HR lists, shifts, leave types, holidays and payroll rules"),
        new(HrAttendance, "HR", "Rosters, and attendance corrections and their approval"),
        new(HrLeave, "HR", "Approve leave and adjust leave balances"),
        new(PayrollRun, "HR", "Salaries, loans, payroll runs, posting and paying them"),
        new(PayrollApprove, "HR", "Approve payroll runs"),

        new(ProductionView, "Production", "See bills of materials, production orders and planning"),
        new(ProductionPlan, "Production", "Bills of materials, work centres, production orders, planning and quality"),
        new(ProductionFloor, "Production", "Work on production orders at the shop floor: operations, materials, output"),
        new(ProductionCosting, "Production", "Production costs and variances, and posting them to the ledger"),
    ];

    public static bool IsKnown(string key) => All.Any(p => p.Key == key);

    /// <summary>
    /// The approval rule: someone may approve, at another user's till, an action that needs <paramref name="permission"/>
    /// when their own role has both <see cref="PosApprove"/> and that permission.
    /// </summary>
    public static bool CanApprove(IReadOnlySet<string> approverPermissions, string permission) =>
        approverPermissions.Contains(PosApprove) && approverPermissions.Contains(permission);

    private static readonly string[] TraineeKeys = [PosSell, InventoryView, ClientsView, TimeClockPunch];
    private static readonly string[] CashierKeys = [.. TraineeKeys, PosReturn, PosDrawer, ClientsEdit, GiftCardsIssue, SyncSend];
    private static readonly string[] ClerkKeys = [.. CashierKeys, InventoryEdit, InventoryAdjust, InventoryCount, InventoryLabels, PurchasingManage, SuppliersEdit];
    private static readonly string[] SupervisorKeys =
        [.. ClerkKeys, PosDiscount, PosVoidLine, PosVoidSale, PosNoSale, PosEndOfDay, PosApprove, InvoicesManage, RmaManage, InventoryTransfer, DashboardView, ReportsView, TimeClockEdit, CrmView];
    private static readonly string[] ManagerKeys =
        [.. SupervisorKeys, InventoryPrices, ReportsStaff, SetupStore, SetupStations, RfidManage, BackupManage, YearEndRun, AuditView, AccountingExport, FinanceView, HrView, HrLeave,
            CrmEdit, MarketingCampaigns, MarketingPromotions, SalesTargets, ProductionView];
    private static readonly string[] AccountantKeys =
        [WebSignIn, DashboardView, ReportsView, AuditView, AccountingExport, ClientsView, InventoryView, FinanceView, FinancePost, FinanceApprove, FinanceClose, FinanceSetup,
            FinancePayables, FinanceBank, FinanceBudget, StoresAll];
    private static readonly string[] HrOfficerKeys = [WebSignIn, ReportsStaff, HrView, HrEdit, HrAttendance, HrLeave, PayrollRun];
    private static readonly string[] ProductionPlannerKeys = [WebSignIn, InventoryView, ProductionView, ProductionPlan, ProductionCosting];
    private static readonly string[] ProductionOperatorKeys = [TimeClockPunch, ProductionView, ProductionFloor];

    public const string TraineeRole = "Trainee";
    public const string CashierRole = "Cashier";
    public const string ClerkRole = "Stock clerk";
    public const string SupervisorRole = "Supervisor";
    public const string ManagerRole = "Manager";
    public const string AdministratorRole = "Administrator";
    public const string SuperAdministratorRole = "Super administrator";
    public const string MarketingRole = "Marketing";

    /// <summary>Marketing works on the web: leads and the pipeline, segments, WhatsApp campaigns and promotions.</summary>
    private static readonly string[] MarketingKeys =
        [WebSignIn, DashboardView, ReportsView, ClientsView, ClientsEdit, CrmView, CrmEdit, MarketingCampaigns, MarketingPromotions];

    public const string AccountantRole = "Accountant";
    public const string HrOfficerRole = "HR officer";
    public const string ProductionPlannerRole = "Production planner";
    public const string ProductionOperatorRole = "Production operator";

    /// <summary>
    /// The roles of a new database. Cashiers, supervisors and managers work at the tills; administrators look after one
    /// store, on the web and at its tills; super administrators see every store.
    /// </summary>
    public static IReadOnlyList<DefaultRole> DefaultRoles { get; } =
    [
        new(TraineeRole, 0, "Practice sales that are not kept", TraineeKeys),
        new(CashierRole, 1, "Sales at the till", CashierKeys),
        new(ClerkRole, 2, "Sales, items and receiving", ClerkKeys),
        new(SupervisorRole, 3, "Approves voids, discounts and End of Day; reports", SupervisorKeys),
        new(ManagerRole, 3, "Runs the store: prices, staff, setup, backups", ManagerKeys),
        new(AdministratorRole, 4, "Everything for one store, at the tills and on the web", [.. All.Select(p => p.Key).Where(k => k != StoresAll)]),
        new(SuperAdministratorRole, 4, "Everything, for every store", [.. All.Select(p => p.Key)]),
        new(MarketingRole, 3, "Leads, the pipeline, segments, WhatsApp campaigns and promotions, on the web", MarketingKeys),
        new(AccountantRole, 3, "Keeps the books on the web: ledger, payables, bank, statements", AccountantKeys),
        new(HrOfficerRole, 3, "Employees, attendance, leave and payroll on the web; someone else approves payroll", HrOfficerKeys),
        new(ProductionPlannerRole, 3, "Bills of materials, production orders, planning, quality and production costs, on the web", ProductionPlannerKeys),
        new(ProductionOperatorRole, 1, "Works production orders on the shop floor screen at a till", ProductionOperatorKeys),
    ];

    /// <summary>The default role for a Retail Plus access level (users brought in by the importer).</summary>
    public static string DefaultRoleForLevel(int level) => level switch
    {
        <= 0 => TraineeRole,
        1 => CashierRole,
        2 => ClerkRole,
        3 => SupervisorRole,
        _ => AdministratorRole,
    };
}
