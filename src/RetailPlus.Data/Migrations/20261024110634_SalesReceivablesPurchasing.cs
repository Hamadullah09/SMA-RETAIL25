using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPlus.Data.Migrations
{
    /// <inheritdoc />
    public partial class SalesReceivablesPurchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Company = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SoldBy = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    InvoiceNumber = table.Column<long>(type: "bigint", nullable: false),
                    CustomerNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(37)", maxLength: 37, nullable: false),
                    Size = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Paid = table.Column<bool>(type: "bit", nullable: false),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DrawerSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    LastSaleAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    OpeningFloat = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PayIns = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PayOuts = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CustomerCount = table.Column<int>(type: "int", nullable: false),
                    NetSales = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax1Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax2Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    NoTaxSales = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Cogs = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CustomerCredits = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    BottleReturns = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalReceipts = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawerSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FastKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Panel = table.Column<int>(type: "int", nullable: false),
                    KeyNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    FixedPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    IsCredit = table.Column<bool>(type: "bit", nullable: false),
                    Taxable1 = table.Column<bool>(type: "bit", nullable: false),
                    Taxable2 = table.Column<bool>(type: "bit", nullable: false),
                    IsCategory = table.Column<bool>(type: "bit", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FastKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FxRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceNumber = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    ClientId = table.Column<int>(type: "int", nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CustomerNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Company = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    SalesPerson = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    LastPaidDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastPaidAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Penalty = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax1Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    Tax2Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    ShipTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SaleId = table.Column<int>(type: "int", nullable: true),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "Rmas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Company = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CustomerPhone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CustomerEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerRmaNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CustomerRmaDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsCustomerRma = table.Column<bool>(type: "bit", nullable: false),
                    Supplier = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SupplierFirstName = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    SupplierLastName = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SupplierPhone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SupplierEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupplierRmaNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SupplierRmaDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsSupplierRma = table.Column<bool>(type: "bit", nullable: false),
                    SetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SetType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rmas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceNumber = table.Column<long>(type: "bigint", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    SoldAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Staff = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ClientId = table.Column<int>(type: "int", nullable: true),
                    ClientName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CustomerNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsOnAccount = table.Column<bool>(type: "bit", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SubtotalDiscount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax1 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax2 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Rounding = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Cogs = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax1Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Tax1Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    Tax2Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Tax2Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    TaxInclusive = table.Column<bool>(type: "bit", nullable: false),
                    Tax2Compound = table.Column<bool>(type: "bit", nullable: false),
                    PointsEarned = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PointsRedeemed = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    VoidedBy = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "StaffPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FromTime = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    ToTime = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Sales = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    OnHandAfter = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    User = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SuspendedSales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    SuspendedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuspendedSales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Tax1Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Tax1Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    Tax2Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Tax2Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenderTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    OpensDrawer = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DrawerMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DrawerSessionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    User = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DrawerSessionId = table.Column<int>(type: "int", nullable: false),
                    TenderType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
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
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    TenderType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AppliedToPenalty = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Size = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    CaseQuantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    OrderQuantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    CostEach = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    OrderCost = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    OnHandAtOrder = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    OnOrderAtOrder = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    BaseStock = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ReorderPoint = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ReorderQuantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    BackOrders = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReorderNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false)
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
                name: "SaleLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaleId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Cogs = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Taxable1 = table.Column<bool>(type: "bit", nullable: false),
                    Taxable2 = table.Column<bool>(type: "bit", nullable: false),
                    Tax1 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax2 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VariantId = table.Column<int>(type: "int", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Commission = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Restock = table.Column<bool>(type: "bit", nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaleId = table.Column<int>(type: "int", nullable: false),
                    TenderType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tendered = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Change = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AuthCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
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
                name: "IX_DrawerMovements_DrawerSessionId",
                table: "DrawerMovements",
                column: "DrawerSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessions_OpenedAt",
                table: "DrawerSessions",
                column: "OpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessions_StationNumber_ClosedAt",
                table: "DrawerSessions",
                columns: new[] { "StationNumber", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessionTenders_DrawerSessionId_TenderType",
                table: "DrawerSessionTenders",
                columns: new[] { "DrawerSessionId", "TenderType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FastKeys_Panel_KeyNumber",
                table: "FastKeys",
                columns: new[] { "Panel", "KeyNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_Currency",
                table: "FxRates",
                column: "Currency",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_InvoiceId",
                table: "InvoicePayments",
                column: "InvoiceId");

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
                name: "IX_Invoices_Kind_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "Kind", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_LastName",
                table: "Invoices",
                column: "LastName");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId_Plu",
                table: "PurchaseOrderLines",
                columns: new[] { "PurchaseOrderId", "Plu" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_LocationId",
                table: "PurchaseOrders",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_Number",
                table: "PurchaseOrders",
                column: "Number",
                unique: true);

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
                name: "IX_SalePayments_SaleId",
                table: "SalePayments",
                column: "SaleId");

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
                name: "IX_Sales_StationNumber_InvoiceNumber",
                table: "Sales",
                columns: new[] { "StationNumber", "InvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_ItemId_At",
                table: "StockAdjustments",
                columns: new[] { "ItemId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_TenderTypes_Name",
                table: "TenderTypes",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerOrders");

            migrationBuilder.DropTable(
                name: "DrawerMovements");

            migrationBuilder.DropTable(
                name: "DrawerSessionTenders");

            migrationBuilder.DropTable(
                name: "FastKeys");

            migrationBuilder.DropTable(
                name: "FxRates");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "InvoicePayments");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "Rmas");

            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "SalePayments");

            migrationBuilder.DropTable(
                name: "StaffPeriods");

            migrationBuilder.DropTable(
                name: "StockAdjustments");

            migrationBuilder.DropTable(
                name: "SuspendedSales");

            migrationBuilder.DropTable(
                name: "TaxConfigurations");

            migrationBuilder.DropTable(
                name: "TenderTypes");

            migrationBuilder.DropTable(
                name: "DrawerSessions");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "Sales");
        }
    }
}
