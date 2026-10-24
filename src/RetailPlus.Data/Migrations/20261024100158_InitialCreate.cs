using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPlus.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateSequence(
                name: "CustomerNumbers",
                schema: "dbo");

            migrationBuilder.CreateSequence(
                name: "InvoiceNumbers",
                schema: "dbo");

            migrationBuilder.CreateSequence(
                name: "PurchaseOrderNumbers",
                schema: "dbo");

            migrationBuilder.CreateSequence(
                name: "RmaNumbers",
                schema: "dbo");

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Birthday = table.Column<DateOnly>(type: "date", nullable: true),
                    Points = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    UsualDiscountPercent = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    ExemptTax1 = table.Column<bool>(type: "bit", nullable: false),
                    ExemptTax2 = table.Column<bool>(type: "bit", nullable: false),
                    PriceLevel = table.Column<int>(type: "int", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    RecurringItemCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    RecurringEvery = table.Column<int>(type: "int", nullable: false),
                    RecurringUnit = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    RecurringNextBilling = table.Column<DateOnly>(type: "date", nullable: true),
                    RecurringLastBilled = table.Column<DateOnly>(type: "date", nullable: true),
                    RecurringMaxBillings = table.Column<int>(type: "int", nullable: false),
                    RecurringTimesBilled = table.Column<int>(type: "int", nullable: false),
                    RecurringActive = table.Column<bool>(type: "bit", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Company = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address1 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address2 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    City = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    State = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Zip = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WorkPhone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Extension = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Fax = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    HomePhone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Number = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LastMailed = table.Column<DateOnly>(type: "date", nullable: true),
                    LastPurchase = table.Column<DateOnly>(type: "date", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ShipTo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhotoPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    SourcePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ToolVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.Id);
                    table.CheckConstraint("CK_Station_Number", "[Number] BETWEEN 1 AND 999");
                });

            migrationBuilder.CreateTable(
                name: "StoreSettingExtras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettingExtras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address1 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address2 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    City = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    State = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Zip = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Fax = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    TaxInclusive = table.Column<bool>(type: "bit", nullable: false),
                    TaxRegistrationNumber = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    Tax1Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Tax1Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    Tax2Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Tax2Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    Tax2Compound = table.Column<bool>(type: "bit", nullable: false),
                    StaffMayDiscount = table.Column<bool>(type: "bit", nullable: false),
                    SupervisorForVoids = table.Column<bool>(type: "bit", nullable: false),
                    TrackStaffSales = table.Column<bool>(type: "bit", nullable: false),
                    CarryOverCityStateZip = table.Column<bool>(type: "bit", nullable: false),
                    ScanRandomWeightBarcodes = table.Column<bool>(type: "bit", nullable: false),
                    UseRmaOnReturns = table.Column<bool>(type: "bit", nullable: false),
                    CashDiscountPercent = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    LimitThc = table.Column<bool>(type: "bit", nullable: false),
                    ThcLimit = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    GiftCardMaximum = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    BonusPointsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PrintPointsOnSlips = table.Column<bool>(type: "bit", nullable: false),
                    PointsPerDollar = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    PointsRequired = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PercentDiscountReward = table.Column<bool>(type: "bit", nullable: false),
                    RewardPercent = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    FixedDiscountReward = table.Column<bool>(type: "bit", nullable: false),
                    RewardAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    InterestRatePerMonth = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    GracePeriodDays = table.Column<int>(type: "int", nullable: false),
                    MinimumTender = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    UseUkDateFormat = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettings", x => x.Id);
                    table.CheckConstraint("CK_StoreSettings_SingleRow", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirstName = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Company = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address1 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address2 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    City = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    State = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Zip = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WorkPhone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Extension = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Fax = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    HomePhone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Number = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LastMailed = table.Column<DateOnly>(type: "date", nullable: true),
                    LastPurchase = table.Column<DateOnly>(type: "date", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ShipTo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhotoPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccessLevel = table.Column<int>(type: "int", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                    table.CheckConstraint("CK_UserAccount_AccessLevel", "[AccessLevel] BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "ImportSourceRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    Entity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    SourceFile = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SourceRecordNumber = table.Column<int>(type: "int", nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                name: "DocumentTexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "StationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationId = table.Column<int>(type: "int", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
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
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    PackQuantity = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Taxable1 = table.Column<bool>(type: "bit", nullable: false),
                    Taxable2 = table.Column<bool>(type: "bit", nullable: false),
                    BinLocation = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    ReadScale = table.Column<bool>(type: "bit", nullable: false),
                    WebDelete = table.Column<bool>(type: "bit", nullable: false),
                    OnHand = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    BaseStock = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ReorderPoint = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ReorderQuantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    OnOrder = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    LastOrderDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastReceivedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastReceivedQuantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    CustomerOrdered = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    OnHandBeforeYearEnd = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    LastUpdated = table.Column<DateOnly>(type: "date", nullable: true),
                    LastCost = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    AverageCost = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Price1 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Price2 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Price3 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Price4 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    BreakQuantity1 = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    BreakQuantity2 = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    BreakQuantity3 = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    MarginPercent = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    SaleStart = table.Column<DateOnly>(type: "date", nullable: true),
                    SaleEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    SalePrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    BonusPricing = table.Column<bool>(type: "bit", nullable: false),
                    BonusBuyQuantity = table.Column<int>(type: "int", nullable: false),
                    BonusGetQuantity = table.Column<int>(type: "int", nullable: false),
                    SubstituteCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    TagAlongCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ParentCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ShipWeight = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ThcWeight = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    MaxCommission = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CommissionType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PhotoPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PosMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InvoiceMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
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
                name: "ItemMatrixColumns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Heading = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    Units = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Gross = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
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
                name: "ItemSerials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    StatusDate = table.Column<DateOnly>(type: "date", nullable: true)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierStockCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    CaseQuantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false)
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
                name: "ItemVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Value1 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Value2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Value3 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OnHand = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KitItemId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    ComponentPlu = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ComponentItemId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

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
                name: "IX_Clients_WorkPhone",
                table: "Clients",
                column: "WorkPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Zip",
                table: "Clients",
                column: "Zip");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTexts_Kind_LocationId",
                table: "DocumentTexts",
                columns: new[] { "Kind", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTexts_LocationId",
                table: "DocumentTexts",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportSourceRows_Entity_EntityId",
                table: "ImportSourceRows",
                columns: new[] { "Entity", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportSourceRows_ImportBatchId",
                table: "ImportSourceRows",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemMatrixColumns_ItemId_Ordinal",
                table: "ItemMatrixColumns",
                columns: new[] { "ItemId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemPeriodSales_ItemId_Year_Kind_Period",
                table: "ItemPeriodSales",
                columns: new[] { "ItemId", "Year", "Kind", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_CategoryId",
                table: "Items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_DepartmentId",
                table: "Items",
                column: "DepartmentId");

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
                name: "IX_ItemSerials_ItemId_SerialNumber",
                table: "ItemSerials",
                columns: new[] { "ItemId", "SerialNumber" });

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
                name: "IX_ItemVariants_Code",
                table: "ItemVariants",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_ItemVariants_ItemId_Ordinal",
                table: "ItemVariants",
                columns: new[] { "ItemId", "Ordinal" },
                unique: true);

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
                name: "IX_Locations_Code",
                table: "Locations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_Number",
                table: "Stations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationSettings_StationId_Section_Key",
                table: "StationSettings",
                columns: new[] { "StationId", "Section", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreSettingExtras_Name",
                table: "StoreSettingExtras",
                column: "Name",
                unique: true);

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
                name: "IX_Suppliers_WorkPhone",
                table: "Suppliers",
                column: "WorkPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Zip",
                table: "Suppliers",
                column: "Zip");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_UserName",
                table: "UserAccounts",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "DocumentTexts");

            migrationBuilder.DropTable(
                name: "ImportSourceRows");

            migrationBuilder.DropTable(
                name: "ItemMatrixColumns");

            migrationBuilder.DropTable(
                name: "ItemPeriodSales");

            migrationBuilder.DropTable(
                name: "ItemSerials");

            migrationBuilder.DropTable(
                name: "ItemSuppliers");

            migrationBuilder.DropTable(
                name: "ItemVariants");

            migrationBuilder.DropTable(
                name: "KitComponents");

            migrationBuilder.DropTable(
                name: "StationSettings");

            migrationBuilder.DropTable(
                name: "StoreSettingExtras");

            migrationBuilder.DropTable(
                name: "StoreSettings");

            migrationBuilder.DropTable(
                name: "UserAccounts");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "Stations");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropSequence(
                name: "CustomerNumbers",
                schema: "dbo");

            migrationBuilder.DropSequence(
                name: "InvoiceNumbers",
                schema: "dbo");

            migrationBuilder.DropSequence(
                name: "PurchaseOrderNumbers",
                schema: "dbo");

            migrationBuilder.DropSequence(
                name: "RmaNumbers",
                schema: "dbo");
        }
    }
}
