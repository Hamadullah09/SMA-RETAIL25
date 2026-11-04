using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPlus.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementFormats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    HasHeader = table.Column<bool>(type: "INTEGER", nullable: false),
                    Delimiter = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false, collation: "NOCASE"),
                    DateColumn = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    DateFormat = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    DescriptionColumn = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    ReferenceColumn = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    AmountColumn = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    DebitColumn = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    CreditColumn = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    BalanceColumn = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementFormats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Birthday = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Points = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    UsualDiscountPercent = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    ExemptTax1 = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExemptTax2 = table.Column<bool>(type: "INTEGER", nullable: false),
                    PriceLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    RecurringItemCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    RecurringEvery = table.Column<int>(type: "INTEGER", nullable: false),
                    RecurringUnit = table.Column<string>(type: "TEXT", maxLength: 6, nullable: true, collation: "NOCASE"),
                    RecurringNextBilling = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RecurringLastBilled = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RecurringMaxBillings = table.Column<int>(type: "INTEGER", nullable: false),
                    RecurringTimesBilled = table.Column<int>(type: "INTEGER", nullable: false),
                    RecurringActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true, collation: "NOCASE"),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Title = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Company = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Address1 = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Address2 = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    City = table.Column<string>(type: "TEXT", maxLength: 18, nullable: true, collation: "NOCASE"),
                    State = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true, collation: "NOCASE"),
                    Zip = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    Country = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    WorkPhone = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true, collation: "NOCASE"),
                    Fax = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    HomePhone = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Email = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    Website = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    Number = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true, collation: "NOCASE"),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    LastMailed = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastPurchase = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true, collation: "NOCASE"),
                    ShipTo = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    Notes = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true, collation: "NOCASE"),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                    table.CheckConstraint("CK_Client_PriceLevel", "[PriceLevel] BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Designations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FastKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Panel = table.Column<int>(type: "INTEGER", nullable: false),
                    KeyNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Plu = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    FixedPrice = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    IsCredit = table.Column<bool>(type: "INTEGER", nullable: false),
                    Taxable1 = table.Column<bool>(type: "INTEGER", nullable: false),
                    Taxable2 = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCategory = table.Column<bool>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FastKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerKind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Size = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    UploadedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AutoPostOnSync = table.Column<bool>(type: "INTEGER", nullable: false),
                    NightlyDays = table.Column<int>(type: "INTEGER", nullable: false),
                    NightlyHour = table.Column<int>(type: "INTEGER", nullable: false),
                    AttachmentMaxKilobytes = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncCursor = table.Column<long>(type: "INTEGER", nullable: false),
                    LastNightlyRunOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FiscalCursor = table.Column<long>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceSettings", x => x.Id);
                    table.CheckConstraint("CK_FinanceSettings_Hour", "[NightlyHour] BETWEEN 0 AND 23");
                });

            migrationBuilder.CreateTable(
                name: "FiscalYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    ClosedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalYears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FxRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, collation: "NOCASE"),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 14, scale: 6, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false, collation: "NOCASE"),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrDepartments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrDepartments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false, collation: "NOCASE"),
                    ToolVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    ReportJson = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false, collation: "NOCASE"),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    DaysPerYear = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccruesMonthly = table.Column<bool>(type: "INTEGER", nullable: false),
                    CarryOverMaxDays = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    Encashable = table.Column<bool>(type: "INTEGER", nullable: false),
                    CountsCalendarDays = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnlyFor = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    MinimumServiceMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Type = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsPosting = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true, collation: "NOCASE"),
                    CashFlow = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerAccounts_LedgerAccounts_ParentId",
                        column: x => x.ParentId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false, collation: "NOCASE"),
                    FiscalYearStartMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false, collation: "NOCASE"),
                    ManualEntriesNeedApproval = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerSettings", x => x.Id);
                    table.CheckConstraint("CK_LedgerSettings_Month", "[FiscalYearStartMonth] BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "NumberCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    LastValue = table.Column<long>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuleSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false, collation: "NOCASE"),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TaxYearStartMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumWage = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    EobiEmployerRate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    EobiEmployeeRate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    SocialSecurityProvince = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    SocialSecurityRate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    SocialSecurityWageCeiling = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    SocialSecurityBasis = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    WorkingDaysPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    HoursPerWeek = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    OvertimeMultiplier = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    OvertimeMinimumMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyRateBasis = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    LatenessRule = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    LatesPerDay = table.Column<int>(type: "INTEGER", nullable: false),
                    DeductAbsences = table.Column<bool>(type: "INTEGER", nullable: false),
                    SurchargeThreshold = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    SurchargeRate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    GratuityEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    GratuityDaysPerYear = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    GratuityMinimumMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    GratuityPartYearMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    GratuityOnBasic = table.Column<bool>(type: "INTEGER", nullable: false),
                    CnicPattern = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    CnicExample = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    IbanPattern = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    IbanExample = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true, collation: "NOCASE"),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuleSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.CheckConstraint("CK_Role_Level", "[Level] BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "StaffPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false, collation: "NOCASE"),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FromDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ToDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FromTime = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true, collation: "NOCASE"),
                    ToTime = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true, collation: "NOCASE"),
                    Sales = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Commission = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Hours = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true, collation: "NOCASE"),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Title = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Company = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Address1 = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Address2 = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    City = table.Column<string>(type: "TEXT", maxLength: 18, nullable: true, collation: "NOCASE"),
                    State = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true, collation: "NOCASE"),
                    Zip = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    Country = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    WorkPhone = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true, collation: "NOCASE"),
                    Fax = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    HomePhone = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Email = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    Website = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    Number = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true, collation: "NOCASE"),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    LastMailed = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastPurchase = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true, collation: "NOCASE"),
                    ShipTo = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    Notes = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true, collation: "NOCASE"),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true, collation: "NOCASE"),
                    FileSha256 = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: true, collation: "NOCASE"),
                    Rows = table.Column<int>(type: "INTEGER", nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    Notes = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackagedThroughUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    SentThroughUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    DownloadCursor = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSentAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: true),
                    LastReceivedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    Tax1Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    Tax1Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    Tax2Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    Tax2Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRuleSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    InputTaxClaimable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true, collation: "NOCASE"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRuleSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenderTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    OpensDrawer = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tombstones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    RowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MergedIntoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tombstones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FiscalPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FiscalYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalPeriods_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportSourceRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    Entity = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceFile = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false, collation: "NOCASE"),
                    SourceRecordNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportSourceRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportSourceRows_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollAllowanceRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    IsTaxable = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExemptPercentOfBasic = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAllowanceRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollAllowanceRules_PayrollRuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalTable: "PayrollRuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollTaxSlabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IncomeFrom = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    IncomeTo = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: true),
                    FixedTax = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollTaxSlabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollTaxSlabs_PayrollRuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalTable: "PayrollRuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Permission = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleGrants_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    AccountCode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountMappings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false, collation: "NOCASE"),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Before = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    After = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    Details = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true, collation: "NOCASE"),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false, collation: "NOCASE"),
                    LedgerAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    OpeningDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankAccounts_LedgerAccounts_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankAccounts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FiscalYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetLines_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetLines_LedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Company = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    OrderDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SoldBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SoldById = table.Column<Guid>(type: "TEXT", nullable: true),
                    InvoiceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    CustomerNumber = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true, collation: "NOCASE"),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Plu = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 37, nullable: false, collation: "NOCASE"),
                    Size = table.Column<string>(type: "TEXT", maxLength: 6, nullable: true, collation: "NOCASE"),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    Paid = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerOrders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DrawerSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    LastSaleAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    OpeningFloat = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    PayIns = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    PayOuts = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    CustomerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NetSales = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Tax1Total = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Tax2Total = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    NoTaxSales = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Cogs = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    CustomerCredits = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    BottleReturns = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    TotalReceipts = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClosedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    ClosedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawerSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawerSessions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GiftCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiftCards_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GiftCards_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<byte>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    CustomerNumber = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true, collation: "NOCASE"),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true, collation: "NOCASE"),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Company = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    InvoiceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    SalesPerson = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SalesPersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastPaidDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastPaidAmount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Penalty = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Tax1Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    Tax2Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    ShipTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    BillTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    Notes = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    Email = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsVoided = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Invoices_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    CreatedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    PostedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    PostedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversalOfId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Points = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InvoiceNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    User = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoyaltyTransactions_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoyaltyTransactions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RfidHubLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false, collation: "NOCASE"),
                    KeyHash = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false, collation: "NOCASE"),
                    KeyHint = table.Column<string>(type: "TEXT", unicode: false, maxLength: 12, nullable: false, collation: "NOCASE"),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    LastAddress = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: true, collation: "NOCASE"),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidHubLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RfidHubLinks_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RfidReaders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false, collation: "NOCASE"),
                    HubKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    Location = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Transport = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    SerialPort = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    BaudRate = table.Column<int>(type: "INTEGER", nullable: false),
                    Address = table.Column<int>(type: "INTEGER", nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    StartFrequency = table.Column<int>(type: "INTEGER", nullable: false),
                    EndFrequency = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomStartKHz = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomSpacing = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomChannels = table.Column<int>(type: "INTEGER", nullable: false),
                    BeeperMode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LinkProfile = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DrmEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AntennaDetectorDb = table.Column<int>(type: "INTEGER", nullable: false),
                    FastTid = table.Column<bool>(type: "INTEGER", nullable: false),
                    RssiThresholdDbm = table.Column<int>(type: "INTEGER", nullable: false),
                    DedupSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    DwellRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    FirmwareVersion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    SettingsAppliedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true, collation: "NOCASE"),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidReaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RfidReaders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Rmas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Plu = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Size = table.Column<string>(type: "TEXT", maxLength: 6, nullable: true, collation: "NOCASE"),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true, collation: "NOCASE"),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Company = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    CustomerPhone = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    CustomerEmail = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    CustomerRmaNumber = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    CustomerRmaDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsCustomerRma = table.Column<bool>(type: "INTEGER", nullable: false),
                    Supplier = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    SupplierFirstName = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true, collation: "NOCASE"),
                    SupplierLastName = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    SupplierPhone = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    SupplierEmail = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    SupplierRmaNumber = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    SupplierRmaDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsSupplierRma = table.Column<bool>(type: "INTEGER", nullable: false),
                    SetDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SetType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    CreatedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rmas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rmas_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    BreakMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    GraceMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Days = table.Column<int>(type: "INTEGER", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shifts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Stations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.Id);
                    table.CheckConstraint("CK_Station_Number", "[Number] BETWEEN 1 AND 999");
                    table.ForeignKey(
                        name: "FK_Stations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreImages_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreSettingExtras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    Value = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettingExtras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreSettingExtras_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Address1 = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Address2 = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    City = table.Column<string>(type: "TEXT", maxLength: 18, nullable: true, collation: "NOCASE"),
                    State = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true, collation: "NOCASE"),
                    Zip = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    Country = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Fax = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    TaxInclusive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TaxRegistrationNumber = table.Column<string>(type: "TEXT", maxLength: 25, nullable: true, collation: "NOCASE"),
                    Tax1Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    Tax1Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    Tax2Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    Tax2Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    Tax2Compound = table.Column<bool>(type: "INTEGER", nullable: false),
                    StaffMayDiscount = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupervisorForVoids = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrackStaffSales = table.Column<bool>(type: "INTEGER", nullable: false),
                    CarryOverCityStateZip = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScanRandomWeightBarcodes = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseRmaOnReturns = table.Column<bool>(type: "INTEGER", nullable: false),
                    CashDiscountPercent = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    LimitThc = table.Column<bool>(type: "INTEGER", nullable: false),
                    ThcLimit = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    GiftCardMaximum = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    BonusPointsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrintPointsOnSlips = table.Column<bool>(type: "INTEGER", nullable: false),
                    PointsPerDollar = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    PointsRequired = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    PercentDiscountReward = table.Column<bool>(type: "INTEGER", nullable: false),
                    RewardPercent = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    FixedDiscountReward = table.Column<bool>(type: "INTEGER", nullable: false),
                    RewardAmount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    InterestRatePerMonth = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    GracePeriodDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumTender = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    UseUkDateFormat = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreSettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SuspendedSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    SuspendedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuspendedSales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuspendedSales_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    AccessLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StaffCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    PinHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    FailedSignIns = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                    table.CheckConstraint("CK_UserAccount_AccessLevel", "[AccessLevel] BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_UserAccounts_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAccounts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierFinances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    SalesTaxNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    IsFiler = table.Column<bool>(type: "INTEGER", nullable: false),
                    WithholdingKind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    WithholdingExempt = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierFinances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierFinances_LedgerAccounts_DefaultAccountId",
                        column: x => x.DefaultAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierFinances_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesTaxRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxRuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false, collation: "NOCASE"),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTaxRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesTaxRates_TaxRuleSets_TaxRuleSetId",
                        column: x => x.TaxRuleSetId,
                        principalTable: "TaxRuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WithholdingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxRuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Section = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    FilerRate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    NonFilerRate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    Threshold = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithholdingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WithholdingRules_TaxRuleSets_TaxRuleSetId",
                        column: x => x.TaxRuleSetId,
                        principalTable: "TaxRuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    FromDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    LineCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatements_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Payments = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    PaidBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    PaidAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRuns_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Payee = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PaidBy = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    NextDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PostAutomatically = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastMadeOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_LedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreFinances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxRuleSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DepositBankAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreFinances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreFinances_BankAccounts_DepositBankAccountId",
                        column: x => x.DepositBankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StoreFinances_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoreFinances_TaxRuleSets_TaxRuleSetId",
                        column: x => x.TaxRuleSetId,
                        principalTable: "TaxRuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DrawerMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DrawerSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    User = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawerMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawerMovements_DrawerSessions_DrawerSessionId",
                        column: x => x.DrawerSessionId,
                        principalTable: "DrawerSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DrawerSessionTenders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DrawerSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenderType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    ForeignAmount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawerSessionTenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawerSessionTenders_DrawerSessions_DrawerSessionId",
                        column: x => x.DrawerSessionId,
                        principalTable: "DrawerSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GiftCardTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GiftCardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InvoiceNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    User = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftCardTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiftCardTransactions_GiftCards_GiftCardId",
                        column: x => x.GiftCardId,
                        principalTable: "GiftCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GiftCardTransactions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Plu = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    TenderType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                    AppliedToPenalty = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    TakenBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    TakenById = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoicePayments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoicePayments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankDeposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DrawerSessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankDeposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankDeposits_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankDeposits_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankDeposits_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FromAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ToAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransfers_BankAccounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_BankAccounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Debit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    PartyKind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    PartyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalLines_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JournalLines_LedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BillDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SupplierReference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false, collation: "NOCASE"),
                    ExchangeRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Paid = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    OriginalBillId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpenseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    PostedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    PostedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    VoidedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierBills_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierBills_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierBills_SupplierBills_OriginalBillId",
                        column: x => x.OriginalBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierBills_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTexts_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentTexts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Plu = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    Barcode = table.Column<string>(type: "TEXT", unicode: false, maxLength: 14, nullable: true, collation: "NOCASE"),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Size = table.Column<string>(type: "TEXT", maxLength: 6, nullable: true, collation: "NOCASE"),
                    PackQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Weight = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    WeightUnit = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true, collation: "NOCASE"),
                    WeightTolerance = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    WeightToleranceUnit = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true, collation: "NOCASE"),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupplierCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Taxable1 = table.Column<bool>(type: "INTEGER", nullable: false),
                    Taxable2 = table.Column<bool>(type: "INTEGER", nullable: false),
                    BinLocation = table.Column<string>(type: "TEXT", maxLength: 25, nullable: true, collation: "NOCASE"),
                    ReadScale = table.Column<bool>(type: "INTEGER", nullable: false),
                    WebDelete = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnHand = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    BaseStock = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    ReorderPoint = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    ReorderQuantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    OnOrder = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    LastOrderDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastReceivedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastReceivedQuantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    CustomerOrdered = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    OnHandBeforeYearEnd = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    LastUpdated = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastCost = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    AverageCost = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    Price1 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Price2 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Price3 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Price4 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    BreakQuantity1 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    BreakQuantity2 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    BreakQuantity3 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    MarginPercent = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    SaleStart = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SaleEnd = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SalePrice = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    BonusPricing = table.Column<bool>(type: "INTEGER", nullable: false),
                    BonusBuyQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    BonusGetQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    SubstituteCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    TagAlongCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    ParentCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    UnitsPerCase = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    ShipWeight = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    ThcWeight = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    Commission = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    MaxCommission = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    CommissionType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true, collation: "NOCASE"),
                    Notes = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    PosMessage = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    InvoiceMessage = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Items_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Items_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Items_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    PostedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    CreatedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReceivedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    ReceivedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SoldAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    Staff = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    StaffId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    CustomerNumber = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IsOnAccount = table.Column<bool>(type: "INTEGER", nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    SubtotalDiscount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Tax1 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Tax2 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Rounding = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Cogs = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Tax1Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    Tax1Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    Tax2Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    Tax2Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    TaxInclusive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Tax2Compound = table.Column<bool>(type: "INTEGER", nullable: false),
                    PointsEarned = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    PointsRedeemed = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    VoidedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    VoidedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    VoidedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    FiscalInvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sales_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sales_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    StartedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    PostedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    PostedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    CancelledBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    UncountedSetToZero = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCounts_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCounts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ToStoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    ShippedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    ShippedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    ReceivedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.CheckConstraint("CK_StockTransfer_Locations", "[FromLocationId] <> [ToLocationId]");
                    table.ForeignKey(
                        name: "FK_StockTransfers_Locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Locations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Stores_ToStoreId",
                        column: x => x.ToStoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RfidAntennas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReaderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PowerDbm = table.Column<int>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidAntennas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RfidAntennas_RfidReaders_ReaderId",
                        column: x => x.ReaderId,
                        principalTable: "RfidReaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StationLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProtectedKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 1000, nullable: true, collation: "NOCASE"),
                    EnrollmentCodeHash = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: true, collation: "NOCASE"),
                    EnrollmentExpiresAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    EnrolledAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    LastUploadAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    LastDownloadAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    DownloadCursor = table.Column<long>(type: "INTEGER", nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StationLinks_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StationLinks_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Section = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Value = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StationSettings_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    FatherOrHusbandName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    Cnic = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Gender = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    MaritalStatus = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    City = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Email = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    EmergencyContactName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    EmergencyContactPhone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    EmergencyContactRelation = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    JoiningDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ConfirmationDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LeavingDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LeavingReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    EmploymentType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    HrDepartmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DesignationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ManagerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TracksAttendance = table.Column<bool>(type: "INTEGER", nullable: false),
                    OvertimeEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    EobiApplies = table.Column<bool>(type: "INTEGER", nullable: false),
                    SocialSecurityApplies = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true, collation: "NOCASE"),
                    Ntn = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    EobiNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    SocialSecurityNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    PriorTaxYear = table.Column<int>(type: "INTEGER", nullable: true),
                    PriorTaxableIncome = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    PriorTaxDeducted = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true, collation: "NOCASE"),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Designations_DesignationId",
                        column: x => x.DesignationId,
                        principalTable: "Designations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_Employees_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_HrDepartments_HrDepartmentId",
                        column: x => x.HrDepartmentId,
                        principalTable: "HrDepartments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TimeClockEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    ClockIn = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    ClockOut = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    EditReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    EditedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    EditedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    EditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeClockEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeClockEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeClockEntries_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false, collation: "NOCASE"),
                    ExchangeRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    WithholdingAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    WithholdingKind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    WithholdingRate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PaymentRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    VoidedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_PaymentRuns_PaymentRunId",
                        column: x => x.PaymentRunId,
                        principalTable: "PaymentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankStatementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    JournalLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MatchedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    MatchedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_BankStatements_BankStatementId",
                        column: x => x.BankStatementId,
                        principalTable: "BankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_JournalLines_JournalLineId",
                        column: x => x.JournalLineId,
                        principalTable: "JournalLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Payee = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PaidBy = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SupplierBillId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecurringExpenseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_LedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_RecurringExpenses_RecurringExpenseId",
                        column: x => x.RecurringExpenseId,
                        principalTable: "RecurringExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Expenses_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommissionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Basis = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionRules_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionRules_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionRules_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionRules_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemMatrixColumns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Heading = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemMatrixColumns", x => x.Id);
                    table.CheckConstraint("CK_ItemMatrixColumn_Ordinal", "[Ordinal] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_ItemMatrixColumns_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemPeriodSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<byte>(type: "INTEGER", nullable: false),
                    Period = table.Column<int>(type: "INTEGER", nullable: false),
                    Units = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    Gross = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPeriodSales", x => x.Id);
                    table.CheckConstraint("CK_ItemPeriodSales_Period", "([Kind] = 1 AND [Period] BETWEEN 1 AND 12) OR ([Kind] = 2 AND [Period] BETWEEN 1 AND 54)");
                    table.ForeignKey(
                        name: "FK_ItemPeriodSales_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemPictures",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPictures", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_ItemPictures_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemSerials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    StatusDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemSerials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemSerials_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemSuppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupplierName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    SupplierStockCode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Cost = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    CaseQuantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemSuppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemSuppliers_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemSuppliers_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Epc = table.Column<string>(type: "TEXT", unicode: false, maxLength: 128, nullable: false, collation: "NOCASE"),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    SoldAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    SoldOnInvoice = table.Column<long>(type: "INTEGER", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemTags_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Value1 = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    Value2 = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    Value3 = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    OnHand = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemVariants_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KitComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KitItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    ComponentPlu = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    ComponentItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitComponents_Items_ComponentItemId",
                        column: x => x.ComponentItemId,
                        principalTable: "Items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KitComponents_Items_KitItemId",
                        column: x => x.KitItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    OnHandAfter = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    DocumentNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true, collation: "NOCASE"),
                    User = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockMovements_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Plu = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    Size = table.Column<string>(type: "TEXT", maxLength: 6, nullable: true, collation: "NOCASE"),
                    CaseQuantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    OrderQuantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    CostEach = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    OrderCost = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    OnHandAtOrder = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    OnOrderAtOrder = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    BaseStock = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    ReorderPoint = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    ReorderQuantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    BackOrders = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupplierName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    ReorderNumber = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    IsFlagged = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FiscalInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true, collation: "NOCASE"),
                    FiscalNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalInvoices_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FiscalInvoices_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Plu = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Cogs = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Department = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true, collation: "NOCASE"),
                    Taxable1 = table.Column<bool>(type: "INTEGER", nullable: false),
                    Taxable2 = table.Column<bool>(type: "INTEGER", nullable: false),
                    Tax1 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Tax2 = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupplierName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    Commission = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Restock = table.Column<bool>(type: "INTEGER", nullable: false),
                    GiftCardNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleLines_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenderType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Tendered = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Change = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    AuthCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true, collation: "NOCASE"),
                    ForeignAmount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "TEXT", precision: 14, scale: 6, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalePayments_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockCountLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockCountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Plu = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    Expected = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    Counted = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    Variance = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    VarianceCost = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    CountedAt = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: false),
                    NotCounted = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCountLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLines_StockCounts_StockCountId",
                        column: x => x.StockCountId,
                        principalTable: "StockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockCountTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockCountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockCountLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Epc = table.Column<string>(type: "TEXT", unicode: false, maxLength: 128, nullable: false, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCountTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountTags_StockCounts_StockCountId",
                        column: x => x.StockCountId,
                        principalTable: "StockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Plu = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    Barcode = table.Column<string>(type: "TEXT", unicode: false, maxLength: 14, nullable: true, collation: "NOCASE"),
                    Size = table.Column<string>(type: "TEXT", maxLength: 6, nullable: true, collation: "NOCASE"),
                    Department = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true, collation: "NOCASE"),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Taxable1 = table.Column<bool>(type: "INTEGER", nullable: false),
                    Taxable2 = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    QuantityShipped = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    DestinationItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtDestination = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    WorkedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LateMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    EarlyMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    OvertimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Approval = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    RequestedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    DecidedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    DecisionNote = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    UploadedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeDocuments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLoans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Instalment = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    FirstMonth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    PaidFromAccount = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLoans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeLoans_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Days = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    MadeBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveAdjustments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaveAdjustments_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FirstDayHalf = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastDayHalf = table.Column<bool>(type: "INTEGER", nullable: false),
                    Days = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    RequestedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    DecidedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    DecisionNote = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true, collation: "NOCASE"),
                    Warnings = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true, collation: "NOCASE"),
                    Employees = table.Column<int>(type: "INTEGER", nullable: false),
                    Gross = table.Column<decimal>(type: "TEXT", precision: 16, scale: 2, nullable: false),
                    Deductions = table.Column<decimal>(type: "TEXT", precision: 16, scale: 2, nullable: false),
                    Net = table.Column<decimal>(type: "TEXT", precision: 16, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "TEXT", precision: 16, scale: 2, nullable: false),
                    EmployerContributions = table.Column<decimal>(type: "TEXT", precision: 16, scale: 2, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    CalculatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    CalculatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    PostedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    PostedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PaidOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "TEXT", precision: 0, nullable: true),
                    PaidBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    PaymentEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RosterDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterDays_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RosterDays_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RosterWeeks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterWeeks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterWeeks_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RosterWeeks_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryStructures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Basic = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    ChangedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryStructures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryStructures_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierPaymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierBillLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxRate = table.Column<decimal>(type: "TEXT", precision: 7, scale: 3, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PurchaseOrderLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierBillLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierBillLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierBillLines_LedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierBillLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierBillLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupplierBillLines_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleLineTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Epc = table.Column<string>(type: "TEXT", unicode: false, maxLength: 128, nullable: false, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLineTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleLineTags_SaleLines_SaleLineId",
                        column: x => x.SaleLineId,
                        principalTable: "SaleLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoanInstalments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Recovered = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanInstalments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanInstalments_EmployeeLoans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "EmployeeLoans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payslips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    EmployeeName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Designation = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    Department = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Cnic = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true, collation: "NOCASE"),
                    PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true, collation: "NOCASE"),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true, collation: "NOCASE"),
                    DaysInPeriod = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysEmployed = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysPresent = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    PaidLeaveDays = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    UnpaidLeaveDays = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    AbsentDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LateDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LateMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: false),
                    Basic = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Gross = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Deductions = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Net = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Taxable = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    EmployerContributions = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    TaxableBefore = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    TaxBefore = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    ProjectedAnnualTaxable = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    ProjectedAnnualTax = table.Column<decimal>(type: "TEXT", precision: 14, scale: 2, nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payslips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payslips_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payslips_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalaryComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalaryStructureId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    AccountCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryComponents_SalaryStructures_SalaryStructureId",
                        column: x => x.SalaryStructureId,
                        principalTable: "SalaryStructures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayslipLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayslipId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 22, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false, collation: "NOCASE"),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: true),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 12, scale: 4, nullable: true),
                    TaxableAmount = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    LoanInstalmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccountCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayslipLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayslipLines_Payslips_PayslipId",
                        column: x => x.PayslipId,
                        principalTable: "Payslips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountMappings_StoreId_Kind_Key",
                table: "AccountMappings",
                columns: new[] { "StoreId", "Kind", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountMappings_UpdatedAtUtc",
                table: "AccountMappings",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_EmployeeId_Date",
                table: "AttendanceCorrections",
                columns: new[] { "EmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_UpdatedAtUtc",
                table: "AttendanceCorrections",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Action_AtUtc",
                table: "AuditEntries",
                columns: new[] { "Action", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_AtUtc",
                table: "AuditEntries",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityName_EntityId",
                table: "AuditEntries",
                columns: new[] { "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_StoreId",
                table: "AuditEntries",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_UpdatedAtUtc",
                table: "AuditEntries",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_UserId_AtUtc",
                table: "AuditEntries",
                columns: new[] { "UserId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_LedgerAccountId",
                table: "BankAccounts",
                column: "LedgerAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_Name",
                table: "BankAccounts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_StoreId",
                table: "BankAccounts",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_UpdatedAtUtc",
                table: "BankAccounts",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BankDeposits_BankAccountId",
                table: "BankDeposits",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankDeposits_DrawerSessionId",
                table: "BankDeposits",
                column: "DrawerSessionId",
                unique: true,
                filter: "[DrawerSessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankDeposits_JournalEntryId",
                table: "BankDeposits",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BankDeposits_Number",
                table: "BankDeposits",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_BankDeposits_StoreId",
                table: "BankDeposits",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_BankDeposits_UpdatedAtUtc",
                table: "BankDeposits",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementFormats_BankName",
                table: "BankStatementFormats",
                column: "BankName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementFormats_UpdatedAtUtc",
                table: "BankStatementFormats",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_BankAccountId_Status_Date",
                table: "BankStatementLines",
                columns: new[] { "BankAccountId", "Status", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_BankStatementId",
                table: "BankStatementLines",
                column: "BankStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_JournalLineId",
                table: "BankStatementLines",
                column: "JournalLineId",
                unique: true,
                filter: "[JournalLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_UpdatedAtUtc",
                table: "BankStatementLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_BankAccountId",
                table: "BankStatements",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_UpdatedAtUtc",
                table: "BankStatements",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_FromAccountId",
                table: "BankTransfers",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_JournalEntryId",
                table: "BankTransfers",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_Number",
                table: "BankTransfers",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_ToAccountId",
                table: "BankTransfers",
                column: "ToAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_UpdatedAtUtc",
                table: "BankTransfers",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_AccountId",
                table: "BudgetLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_FiscalYearId_AccountId_StoreId_Month",
                table: "BudgetLines",
                columns: new[] { "FiscalYearId", "AccountId", "StoreId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_StoreId",
                table: "BudgetLines",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_UpdatedAtUtc",
                table: "BudgetLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UpdatedAtUtc",
                table: "Categories",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_AccountNumber",
                table: "Clients",
                column: "AccountNumber",
                unique: true,
                filter: "[AccountNumber] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Company",
                table: "Clients",
                column: "Company");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Email",
                table: "Clients",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_LastName",
                table: "Clients",
                column: "LastName");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_LastPurchase",
                table: "Clients",
                column: "LastPurchase",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Number",
                table: "Clients",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_State_City",
                table: "Clients",
                columns: new[] { "State", "City" });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Type",
                table: "Clients",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_UpdatedAtUtc",
                table: "Clients",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_WorkPhone",
                table: "Clients",
                column: "WorkPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Zip",
                table: "Clients",
                column: "Zip");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_CategoryId",
                table: "CommissionRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_DepartmentId",
                table: "CommissionRules",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_ItemId",
                table: "CommissionRules",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_UpdatedAtUtc",
                table: "CommissionRules",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_UserId",
                table: "CommissionRules",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrders_Company",
                table: "CustomerOrders",
                column: "Company");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrders_Description",
                table: "CustomerOrders",
                column: "Description");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrders_OrderDate",
                table: "CustomerOrders",
                column: "OrderDate");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrders_StoreId",
                table: "CustomerOrders",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOrders_UpdatedAtUtc",
                table: "CustomerOrders",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_UpdatedAtUtc",
                table: "Departments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Designations_Name",
                table: "Designations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Designations_UpdatedAtUtc",
                table: "Designations",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTexts_Kind_LocationId",
                table: "DocumentTexts",
                columns: new[] { "Kind", "LocationId" },
                unique: true,
                filter: "[LocationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTexts_LocationId",
                table: "DocumentTexts",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTexts_StoreId_Kind",
                table: "DocumentTexts",
                columns: new[] { "StoreId", "Kind" },
                unique: true,
                filter: "[LocationId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTexts_UpdatedAtUtc",
                table: "DocumentTexts",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerMovements_DrawerSessionId",
                table: "DrawerMovements",
                column: "DrawerSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerMovements_UpdatedAtUtc",
                table: "DrawerMovements",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessions_OpenedAt",
                table: "DrawerSessions",
                column: "OpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessions_StationNumber_ClosedAt",
                table: "DrawerSessions",
                columns: new[] { "StationNumber", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessions_StoreId",
                table: "DrawerSessions",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessions_UpdatedAtUtc",
                table: "DrawerSessions",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessionTenders_DrawerSessionId_TenderType",
                table: "DrawerSessionTenders",
                columns: new[] { "DrawerSessionId", "TenderType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessionTenders_UpdatedAtUtc",
                table: "DrawerSessionTenders",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_EmployeeId",
                table: "EmployeeDocuments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_UpdatedAtUtc",
                table: "EmployeeDocuments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoans_EmployeeId_Status",
                table: "EmployeeLoans",
                columns: new[] { "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoans_Number",
                table: "EmployeeLoans",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoans_UpdatedAtUtc",
                table: "EmployeeLoans",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Code",
                table: "Employees",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DesignationId",
                table: "Employees",
                column: "DesignationId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_FullName",
                table: "Employees",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_HrDepartmentId",
                table: "Employees",
                column: "HrDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ManagerId",
                table: "Employees",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ShiftId",
                table: "Employees",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_StoreId",
                table: "Employees",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_UpdatedAtUtc",
                table: "Employees",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_UserAccountId",
                table: "Employees",
                column: "UserAccountId",
                unique: true,
                filter: "[UserAccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AccountId",
                table: "Expenses",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BankAccountId",
                table: "Expenses",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_JournalEntryId",
                table: "Expenses",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_Number",
                table: "Expenses",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_RecurringExpenseId",
                table: "Expenses",
                column: "RecurringExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_StoreId_Date",
                table: "Expenses",
                columns: new[] { "StoreId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SupplierBillId",
                table: "Expenses",
                column: "SupplierBillId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SupplierId",
                table: "Expenses",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_UpdatedAtUtc",
                table: "Expenses",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FastKeys_Panel_KeyNumber",
                table: "FastKeys",
                columns: new[] { "Panel", "KeyNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FastKeys_UpdatedAtUtc",
                table: "FastKeys",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAttachments_OwnerKind_OwnerId",
                table: "FinanceAttachments",
                columns: new[] { "OwnerKind", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAttachments_UpdatedAtUtc",
                table: "FinanceAttachments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceSettings_UpdatedAtUtc",
                table: "FinanceSettings",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalInvoices_SaleId",
                table: "FiscalInvoices",
                column: "SaleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalInvoices_Status_NextAttemptUtc",
                table: "FiscalInvoices",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalInvoices_StoreId",
                table: "FiscalInvoices",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalInvoices_UpdatedAtUtc",
                table: "FiscalInvoices",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_FiscalYearId_Number",
                table: "FiscalPeriods",
                columns: new[] { "FiscalYearId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_StartDate",
                table: "FiscalPeriods",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_UpdatedAtUtc",
                table: "FiscalPeriods",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_StartDate",
                table: "FiscalYears",
                column: "StartDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_UpdatedAtUtc",
                table: "FiscalYears",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_Currency",
                table: "FxRates",
                column: "Currency",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_UpdatedAtUtc",
                table: "FxRates",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_ClientId",
                table: "GiftCards",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_Number",
                table: "GiftCards",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_StoreId",
                table: "GiftCards",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_UpdatedAtUtc",
                table: "GiftCards",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCardTransactions_GiftCardId_At",
                table: "GiftCardTransactions",
                columns: new[] { "GiftCardId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_GiftCardTransactions_SaleId",
                table: "GiftCardTransactions",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCardTransactions_StoreId",
                table: "GiftCardTransactions",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCardTransactions_UpdatedAtUtc",
                table: "GiftCardTransactions",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Country_Date",
                table: "Holidays",
                columns: new[] { "Country", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_UpdatedAtUtc",
                table: "Holidays",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HrDepartments_Name",
                table: "HrDepartments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HrDepartments_UpdatedAtUtc",
                table: "HrDepartments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ImportSourceRows_Entity_EntityId",
                table: "ImportSourceRows",
                columns: new[] { "Entity", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportSourceRows_ImportBatchId",
                table: "ImportSourceRows",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_UpdatedAtUtc",
                table: "InvoiceLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_InvoiceId",
                table: "InvoicePayments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_StoreId",
                table: "InvoicePayments",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_UpdatedAtUtc",
                table: "InvoicePayments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_AccountNumber",
                table: "Invoices",
                column: "AccountNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ClientId",
                table: "Invoices",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Company",
                table: "Invoices",
                column: "Company");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceDate",
                table: "Invoices",
                column: "InvoiceDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_LastName",
                table: "Invoices",
                column: "LastName");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_StoreId_Kind_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "StoreId", "Kind", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UpdatedAtUtc",
                table: "Invoices",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItemMatrixColumns_ItemId_Ordinal",
                table: "ItemMatrixColumns",
                columns: new[] { "ItemId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemMatrixColumns_UpdatedAtUtc",
                table: "ItemMatrixColumns",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPeriodSales_ItemId_Year_Kind_Period_StationNumber",
                table: "ItemPeriodSales",
                columns: new[] { "ItemId", "Year", "Kind", "Period", "StationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemPeriodSales_UpdatedAtUtc",
                table: "ItemPeriodSales",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPictures_UpdatedAtUtc",
                table: "ItemPictures",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CategoryId",
                table: "Items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_DepartmentId",
                table: "Items",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_LocationId_Barcode",
                table: "Items",
                columns: new[] { "LocationId", "Barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_LocationId_DepartmentId_Plu",
                table: "Items",
                columns: new[] { "LocationId", "DepartmentId", "Plu" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_LocationId_Name",
                table: "Items",
                columns: new[] { "LocationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_LocationId_Plu",
                table: "Items",
                columns: new[] { "LocationId", "Plu" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Items_LocationId_SupplierCode",
                table: "Items",
                columns: new[] { "LocationId", "SupplierCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_LocationId_SupplierId_Plu",
                table: "Items",
                columns: new[] { "LocationId", "SupplierId", "Plu" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_SupplierId",
                table: "Items",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_UpdatedAtUtc",
                table: "Items",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItemSerials_ItemId_SerialNumber",
                table: "ItemSerials",
                columns: new[] { "ItemId", "SerialNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSerials_UpdatedAtUtc",
                table: "ItemSerials",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItemSuppliers_ItemId_Rank",
                table: "ItemSuppliers",
                columns: new[] { "ItemId", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemSuppliers_SupplierId",
                table: "ItemSuppliers",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemSuppliers_UpdatedAtUtc",
                table: "ItemSuppliers",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTags_Epc",
                table: "ItemTags",
                column: "Epc",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemTags_ItemId_Status",
                table: "ItemTags",
                columns: new[] { "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemTags_UpdatedAtUtc",
                table: "ItemTags",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItemVariants_Code",
                table: "ItemVariants",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_ItemVariants_ItemId_Ordinal",
                table: "ItemVariants",
                columns: new[] { "ItemId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemVariants_UpdatedAtUtc",
                table: "ItemVariants",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_Date_Status",
                table: "JournalEntries",
                columns: new[] { "Date", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_Number",
                table: "JournalEntries",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_Source",
                table: "JournalEntries",
                column: "Source",
                unique: true,
                filter: "[Source] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_StoreId",
                table: "JournalEntries",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_UpdatedAtUtc",
                table: "JournalEntries",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_AccountId_StoreId",
                table: "JournalLines",
                columns: new[] { "AccountId", "StoreId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_JournalEntryId",
                table: "JournalLines",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_PartyKind_PartyId",
                table: "JournalLines",
                columns: new[] { "PartyKind", "PartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_StoreId",
                table: "JournalLines",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_UpdatedAtUtc",
                table: "JournalLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_KitComponents_ComponentItemId",
                table: "KitComponents",
                column: "ComponentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_KitComponents_KitItemId_Ordinal",
                table: "KitComponents",
                columns: new[] { "KitItemId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KitComponents_UpdatedAtUtc",
                table: "KitComponents",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAdjustments_EmployeeId_Year",
                table: "LeaveAdjustments",
                columns: new[] { "EmployeeId", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAdjustments_LeaveTypeId",
                table: "LeaveAdjustments",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAdjustments_UpdatedAtUtc",
                table: "LeaveAdjustments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_EmployeeId_FromDate",
                table: "LeaveRequests",
                columns: new[] { "EmployeeId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_LeaveTypeId",
                table: "LeaveRequests",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_Status_FromDate",
                table: "LeaveRequests",
                columns: new[] { "Status", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_UpdatedAtUtc",
                table: "LeaveRequests",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_Country_Code",
                table: "LeaveTypes",
                columns: new[] { "Country", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_UpdatedAtUtc",
                table: "LeaveTypes",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_Code",
                table: "LedgerAccounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_ParentId",
                table: "LedgerAccounts",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_Role",
                table: "LedgerAccounts",
                column: "Role",
                unique: true,
                filter: "[Role] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_UpdatedAtUtc",
                table: "LedgerAccounts",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerSettings_UpdatedAtUtc",
                table: "LedgerSettings",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LoanInstalments_LoanId_Number",
                table: "LoanInstalments",
                columns: new[] { "LoanId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanInstalments_Month",
                table: "LoanInstalments",
                column: "Month");

            migrationBuilder.CreateIndex(
                name: "IX_LoanInstalments_UpdatedAtUtc",
                table: "LoanInstalments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_StoreId_Code",
                table: "Locations",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_UpdatedAtUtc",
                table: "Locations",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_ClientId_At",
                table: "LoyaltyTransactions",
                columns: new[] { "ClientId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_SaleId",
                table: "LoyaltyTransactions",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_StoreId",
                table: "LoyaltyTransactions",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_UpdatedAtUtc",
                table: "LoyaltyTransactions",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NumberCounters_Kind_StationNumber",
                table: "NumberCounters",
                columns: new[] { "Kind", "StationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NumberCounters_UpdatedAtUtc",
                table: "NumberCounters",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRuns_BankAccountId",
                table: "PaymentRuns",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRuns_Number",
                table: "PaymentRuns",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRuns_UpdatedAtUtc",
                table: "PaymentRuns",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAllowanceRules_RuleSetId_Code",
                table: "PayrollAllowanceRules",
                columns: new[] { "RuleSetId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAllowanceRules_UpdatedAtUtc",
                table: "PayrollAllowanceRules",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuleSets_Country_EffectiveFrom",
                table: "PayrollRuleSets",
                columns: new[] { "Country", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuleSets_UpdatedAtUtc",
                table: "PayrollRuleSets",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_EmployeeId",
                table: "PayrollRuns",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_Number",
                table: "PayrollRuns",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_StoreId",
                table: "PayrollRuns",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_UpdatedAtUtc",
                table: "PayrollRuns",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_Year_Month",
                table: "PayrollRuns",
                columns: new[] { "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTaxSlabs_RuleSetId_IncomeFrom",
                table: "PayrollTaxSlabs",
                columns: new[] { "RuleSetId", "IncomeFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTaxSlabs_UpdatedAtUtc",
                table: "PayrollTaxSlabs",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PayslipLines_PayslipId",
                table: "PayslipLines",
                column: "PayslipId");

            migrationBuilder.CreateIndex(
                name: "IX_PayslipLines_UpdatedAtUtc",
                table: "PayslipLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_EmployeeId",
                table: "Payslips",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_PayrollRunId_EmployeeId",
                table: "Payslips",
                columns: new[] { "PayrollRunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_UpdatedAtUtc",
                table: "Payslips",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId_Plu",
                table: "PurchaseOrderLines",
                columns: new[] { "PurchaseOrderId", "Plu" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_UpdatedAtUtc",
                table: "PurchaseOrderLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_LocationId",
                table: "PurchaseOrders",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_StoreId_Number",
                table: "PurchaseOrders",
                columns: new[] { "StoreId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_UpdatedAtUtc",
                table: "PurchaseOrders",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_AccountId",
                table: "RecurringExpenses",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_BankAccountId",
                table: "RecurringExpenses",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_IsActive_NextDate",
                table: "RecurringExpenses",
                columns: new[] { "IsActive", "NextDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_StoreId",
                table: "RecurringExpenses",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_SupplierId",
                table: "RecurringExpenses",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_UpdatedAtUtc",
                table: "RecurringExpenses",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RfidAntennas_ReaderId_Port",
                table: "RfidAntennas",
                columns: new[] { "ReaderId", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RfidAntennas_StationNumber",
                table: "RfidAntennas",
                column: "StationNumber");

            migrationBuilder.CreateIndex(
                name: "IX_RfidAntennas_UpdatedAtUtc",
                table: "RfidAntennas",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RfidHubLinks_KeyHash",
                table: "RfidHubLinks",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RfidHubLinks_StoreId",
                table: "RfidHubLinks",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_RfidHubLinks_UpdatedAtUtc",
                table: "RfidHubLinks",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RfidReaders_HubKey",
                table: "RfidReaders",
                column: "HubKey");

            migrationBuilder.CreateIndex(
                name: "IX_RfidReaders_StoreId_Name",
                table: "RfidReaders",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RfidReaders_UpdatedAtUtc",
                table: "RfidReaders",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Rmas_StoreId",
                table: "Rmas",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Rmas_UpdatedAtUtc",
                table: "Rmas",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RoleGrants_RoleId_Permission",
                table: "RoleGrants",
                columns: new[] { "RoleId", "Permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleGrants_UpdatedAtUtc",
                table: "RoleGrants",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_UpdatedAtUtc",
                table: "Roles",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RosterDays_EmployeeId_Date",
                table: "RosterDays",
                columns: new[] { "EmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterDays_ShiftId",
                table: "RosterDays",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterDays_UpdatedAtUtc",
                table: "RosterDays",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RosterWeeks_EmployeeId_WeekStart",
                table: "RosterWeeks",
                columns: new[] { "EmployeeId", "WeekStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterWeeks_ShiftId",
                table: "RosterWeeks",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterWeeks_UpdatedAtUtc",
                table: "RosterWeeks",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryComponents_SalaryStructureId",
                table: "SalaryComponents",
                column: "SalaryStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryComponents_UpdatedAtUtc",
                table: "SalaryComponents",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructures_EmployeeId_EffectiveFrom",
                table: "SalaryStructures",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStructures_UpdatedAtUtc",
                table: "SalaryStructures",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_ItemId",
                table: "SaleLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_Plu",
                table: "SaleLines",
                column: "Plu");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_UpdatedAtUtc",
                table: "SaleLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLineTags_Epc",
                table: "SaleLineTags",
                column: "Epc");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLineTags_SaleLineId",
                table: "SaleLineTags",
                column: "SaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLineTags_UpdatedAtUtc",
                table: "SaleLineTags",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SalePayments_SaleId",
                table: "SalePayments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SalePayments_UpdatedAtUtc",
                table: "SalePayments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ClientId",
                table: "Sales",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_InvoiceNumber",
                table: "Sales",
                column: "InvoiceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_LocationId",
                table: "Sales",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SoldAt",
                table: "Sales",
                column: "SoldAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_StaffId",
                table: "Sales",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_StationNumber_InvoiceNumber",
                table: "Sales",
                columns: new[] { "StationNumber", "InvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_StoreId",
                table: "Sales",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_UpdatedAtUtc",
                table: "Sales",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxRates_TaxRuleSetId",
                table: "SalesTaxRates",
                column: "TaxRuleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxRates_UpdatedAtUtc",
                table: "SalesTaxRates",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_StoreId",
                table: "Shifts",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_UpdatedAtUtc",
                table: "Shifts",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPeriods_UpdatedAtUtc",
                table: "StaffPeriods",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StationLinks_EnrollmentCodeHash",
                table: "StationLinks",
                column: "EnrollmentCodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_StationLinks_StationId",
                table: "StationLinks",
                column: "StationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationLinks_StationNumber",
                table: "StationLinks",
                column: "StationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationLinks_StoreId",
                table: "StationLinks",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StationLinks_UpdatedAtUtc",
                table: "StationLinks",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_Number",
                table: "Stations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_StoreId",
                table: "Stations",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_UpdatedAtUtc",
                table: "Stations",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StationSettings_StationId_Section_Key",
                table: "StationSettings",
                columns: new[] { "StationId", "Section", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationSettings_UpdatedAtUtc",
                table: "StationSettings",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_ItemId",
                table: "StockCountLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_StockCountId_ItemId_VariantId",
                table: "StockCountLines",
                columns: new[] { "StockCountId", "ItemId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_UpdatedAtUtc",
                table: "StockCountLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_LocationId_Status",
                table: "StockCounts",
                columns: new[] { "LocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_StationNumber_Number",
                table: "StockCounts",
                columns: new[] { "StationNumber", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_StoreId",
                table: "StockCounts",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_UpdatedAtUtc",
                table: "StockCounts",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountTags_StockCountId_Epc",
                table: "StockCountTags",
                columns: new[] { "StockCountId", "Epc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCountTags_UpdatedAtUtc",
                table: "StockCountTags",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ItemId_At",
                table: "StockMovements",
                columns: new[] { "ItemId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_Kind_At",
                table: "StockMovements",
                columns: new[] { "Kind", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StoreId",
                table: "StockMovements",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_UpdatedAtUtc",
                table: "StockMovements",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_StockTransferId_LineNumber",
                table: "StockTransferLines",
                columns: new[] { "StockTransferId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_UpdatedAtUtc",
                table: "StockTransferLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_FromLocationId",
                table: "StockTransfers",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_StationNumber_Number",
                table: "StockTransfers",
                columns: new[] { "StationNumber", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_StoreId",
                table: "StockTransfers",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ToLocationId",
                table: "StockTransfers",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ToStoreId_Status",
                table: "StockTransfers",
                columns: new[] { "ToStoreId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_UpdatedAtUtc",
                table: "StockTransfers",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StoreFinances_DepositBankAccountId",
                table: "StoreFinances",
                column: "DepositBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreFinances_StoreId",
                table: "StoreFinances",
                column: "StoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreFinances_TaxRuleSetId",
                table: "StoreFinances",
                column: "TaxRuleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreFinances_UpdatedAtUtc",
                table: "StoreFinances",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StoreImages_StoreId_Kind",
                table: "StoreImages",
                columns: new[] { "StoreId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreImages_UpdatedAtUtc",
                table: "StoreImages",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_Code",
                table: "Stores",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_UpdatedAtUtc",
                table: "Stores",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSettingExtras_StoreId_Name",
                table: "StoreSettingExtras",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreSettingExtras_UpdatedAtUtc",
                table: "StoreSettingExtras",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSettings_StoreId",
                table: "StoreSettings",
                column: "StoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreSettings_UpdatedAtUtc",
                table: "StoreSettings",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillLines_AccountId",
                table: "SupplierBillLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillLines_ItemId",
                table: "SupplierBillLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillLines_PurchaseOrderId",
                table: "SupplierBillLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillLines_PurchaseOrderLineId",
                table: "SupplierBillLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillLines_SupplierBillId",
                table: "SupplierBillLines",
                column: "SupplierBillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillLines_UpdatedAtUtc",
                table: "SupplierBillLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_JournalEntryId",
                table: "SupplierBills",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_Number",
                table: "SupplierBills",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_OriginalBillId",
                table: "SupplierBills",
                column: "OriginalBillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_StoreId_DueDate",
                table: "SupplierBills",
                columns: new[] { "StoreId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_SupplierId_Status",
                table: "SupplierBills",
                columns: new[] { "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_UpdatedAtUtc",
                table: "SupplierBills",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierFinances_DefaultAccountId",
                table: "SupplierFinances",
                column: "DefaultAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierFinances_SupplierId",
                table: "SupplierFinances",
                column: "SupplierId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierFinances_UpdatedAtUtc",
                table: "SupplierFinances",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_SupplierBillId",
                table: "SupplierPaymentAllocations",
                column: "SupplierBillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_SupplierPaymentId",
                table: "SupplierPaymentAllocations",
                column: "SupplierPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_UpdatedAtUtc",
                table: "SupplierPaymentAllocations",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_BankAccountId",
                table: "SupplierPayments",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_JournalEntryId",
                table: "SupplierPayments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_Number",
                table: "SupplierPayments",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_PaymentRunId",
                table: "SupplierPayments",
                column: "PaymentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_StoreId",
                table: "SupplierPayments",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierId_Date",
                table: "SupplierPayments",
                columns: new[] { "SupplierId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_UpdatedAtUtc",
                table: "SupplierPayments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_AccountNumber",
                table: "Suppliers",
                column: "AccountNumber",
                unique: true,
                filter: "[AccountNumber] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Company",
                table: "Suppliers",
                column: "Company");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Email",
                table: "Suppliers",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_LastName",
                table: "Suppliers",
                column: "LastName");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_LastPurchase",
                table: "Suppliers",
                column: "LastPurchase",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Number",
                table: "Suppliers",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_State_City",
                table: "Suppliers",
                columns: new[] { "State", "City" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Type",
                table: "Suppliers",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_UpdatedAtUtc",
                table: "Suppliers",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_WorkPhone",
                table: "Suppliers",
                column: "WorkPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Zip",
                table: "Suppliers",
                column: "Zip");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSales_StoreId",
                table: "SuspendedSales",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSales_UpdatedAtUtc",
                table: "SuspendedSales",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SyncBatches_BatchId",
                table: "SyncBatches",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncBatches_Status",
                table: "SyncBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SyncBatches_StoreId_StationNumber_ReceivedAtUtc",
                table: "SyncBatches",
                columns: new[] { "StoreId", "StationNumber", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncBatches_UpdatedAtUtc",
                table: "SyncBatches",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SyncStates_UpdatedAtUtc",
                table: "SyncStates",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TaxConfigurations_UpdatedAtUtc",
                table: "TaxConfigurations",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRuleSets_Name",
                table: "TaxRuleSets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRuleSets_UpdatedAtUtc",
                table: "TaxRuleSets",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TenderTypes_Name",
                table: "TenderTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenderTypes_UpdatedAtUtc",
                table: "TenderTypes",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TimeClockEntries_ClockIn",
                table: "TimeClockEntries",
                column: "ClockIn");

            migrationBuilder.CreateIndex(
                name: "IX_TimeClockEntries_StoreId",
                table: "TimeClockEntries",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeClockEntries_UpdatedAtUtc",
                table: "TimeClockEntries",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TimeClockEntries_UserId_ClockIn",
                table: "TimeClockEntries",
                columns: new[] { "UserId", "ClockIn" });

            migrationBuilder.CreateIndex(
                name: "IX_Tombstones_EntityName_RowId",
                table: "Tombstones",
                columns: new[] { "EntityName", "RowId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tombstones_UpdatedAtUtc",
                table: "Tombstones",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_RoleId",
                table: "UserAccounts",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_StaffCode",
                table: "UserAccounts",
                column: "StaffCode",
                unique: true,
                filter: "[StaffCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_StoreId",
                table: "UserAccounts",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_UpdatedAtUtc",
                table: "UserAccounts",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_UserName",
                table: "UserAccounts",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WithholdingRules_TaxRuleSetId_Kind",
                table: "WithholdingRules",
                columns: new[] { "TaxRuleSetId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WithholdingRules_UpdatedAtUtc",
                table: "WithholdingRules",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountMappings");

            migrationBuilder.DropTable(
                name: "AttendanceCorrections");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "BankDeposits");

            migrationBuilder.DropTable(
                name: "BankStatementFormats");

            migrationBuilder.DropTable(
                name: "BankStatementLines");

            migrationBuilder.DropTable(
                name: "BankTransfers");

            migrationBuilder.DropTable(
                name: "BudgetLines");

            migrationBuilder.DropTable(
                name: "CommissionRules");

            migrationBuilder.DropTable(
                name: "CustomerOrders");

            migrationBuilder.DropTable(
                name: "DocumentTexts");

            migrationBuilder.DropTable(
                name: "DrawerMovements");

            migrationBuilder.DropTable(
                name: "DrawerSessionTenders");

            migrationBuilder.DropTable(
                name: "EmployeeDocuments");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "FastKeys");

            migrationBuilder.DropTable(
                name: "FinanceAttachments");

            migrationBuilder.DropTable(
                name: "FinanceSettings");

            migrationBuilder.DropTable(
                name: "FiscalInvoices");

            migrationBuilder.DropTable(
                name: "FiscalPeriods");

            migrationBuilder.DropTable(
                name: "FxRates");

            migrationBuilder.DropTable(
                name: "GiftCardTransactions");

            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "ImportSourceRows");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "InvoicePayments");

            migrationBuilder.DropTable(
                name: "ItemMatrixColumns");

            migrationBuilder.DropTable(
                name: "ItemPeriodSales");

            migrationBuilder.DropTable(
                name: "ItemPictures");

            migrationBuilder.DropTable(
                name: "ItemSerials");

            migrationBuilder.DropTable(
                name: "ItemSuppliers");

            migrationBuilder.DropTable(
                name: "ItemTags");

            migrationBuilder.DropTable(
                name: "ItemVariants");

            migrationBuilder.DropTable(
                name: "KitComponents");

            migrationBuilder.DropTable(
                name: "LeaveAdjustments");

            migrationBuilder.DropTable(
                name: "LeaveRequests");

            migrationBuilder.DropTable(
                name: "LedgerSettings");

            migrationBuilder.DropTable(
                name: "LoanInstalments");

            migrationBuilder.DropTable(
                name: "LoyaltyTransactions");

            migrationBuilder.DropTable(
                name: "NumberCounters");

            migrationBuilder.DropTable(
                name: "PayrollAllowanceRules");

            migrationBuilder.DropTable(
                name: "PayrollTaxSlabs");

            migrationBuilder.DropTable(
                name: "PayslipLines");

            migrationBuilder.DropTable(
                name: "RfidAntennas");

            migrationBuilder.DropTable(
                name: "RfidHubLinks");

            migrationBuilder.DropTable(
                name: "Rmas");

            migrationBuilder.DropTable(
                name: "RoleGrants");

            migrationBuilder.DropTable(
                name: "RosterDays");

            migrationBuilder.DropTable(
                name: "RosterWeeks");

            migrationBuilder.DropTable(
                name: "SalaryComponents");

            migrationBuilder.DropTable(
                name: "SaleLineTags");

            migrationBuilder.DropTable(
                name: "SalePayments");

            migrationBuilder.DropTable(
                name: "SalesTaxRates");

            migrationBuilder.DropTable(
                name: "StaffPeriods");

            migrationBuilder.DropTable(
                name: "StationLinks");

            migrationBuilder.DropTable(
                name: "StationSettings");

            migrationBuilder.DropTable(
                name: "StockCountLines");

            migrationBuilder.DropTable(
                name: "StockCountTags");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "StockTransferLines");

            migrationBuilder.DropTable(
                name: "StoreFinances");

            migrationBuilder.DropTable(
                name: "StoreImages");

            migrationBuilder.DropTable(
                name: "StoreSettingExtras");

            migrationBuilder.DropTable(
                name: "StoreSettings");

            migrationBuilder.DropTable(
                name: "SupplierBillLines");

            migrationBuilder.DropTable(
                name: "SupplierFinances");

            migrationBuilder.DropTable(
                name: "SupplierPaymentAllocations");

            migrationBuilder.DropTable(
                name: "SuspendedSales");

            migrationBuilder.DropTable(
                name: "SyncBatches");

            migrationBuilder.DropTable(
                name: "SyncStates");

            migrationBuilder.DropTable(
                name: "TaxConfigurations");

            migrationBuilder.DropTable(
                name: "TenderTypes");

            migrationBuilder.DropTable(
                name: "TimeClockEntries");

            migrationBuilder.DropTable(
                name: "Tombstones");

            migrationBuilder.DropTable(
                name: "WithholdingRules");

            migrationBuilder.DropTable(
                name: "BankStatements");

            migrationBuilder.DropTable(
                name: "JournalLines");

            migrationBuilder.DropTable(
                name: "DrawerSessions");

            migrationBuilder.DropTable(
                name: "RecurringExpenses");

            migrationBuilder.DropTable(
                name: "FiscalYears");

            migrationBuilder.DropTable(
                name: "GiftCards");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "LeaveTypes");

            migrationBuilder.DropTable(
                name: "EmployeeLoans");

            migrationBuilder.DropTable(
                name: "PayrollRuleSets");

            migrationBuilder.DropTable(
                name: "Payslips");

            migrationBuilder.DropTable(
                name: "RfidReaders");

            migrationBuilder.DropTable(
                name: "SalaryStructures");

            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "Stations");

            migrationBuilder.DropTable(
                name: "StockCounts");

            migrationBuilder.DropTable(
                name: "StockTransfers");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "SupplierBills");

            migrationBuilder.DropTable(
                name: "SupplierPayments");

            migrationBuilder.DropTable(
                name: "TaxRuleSets");

            migrationBuilder.DropTable(
                name: "PayrollRuns");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "PaymentRuns");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropTable(
                name: "Designations");

            migrationBuilder.DropTable(
                name: "HrDepartments");

            migrationBuilder.DropTable(
                name: "Shifts");

            migrationBuilder.DropTable(
                name: "UserAccounts");

            migrationBuilder.DropTable(
                name: "LedgerAccounts");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Stores");
        }
    }
}
