using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPlus.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
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
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FastKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Panel = table.Column<int>(type: "int", nullable: false),
                    KeyNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    FixedPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    IsCredit = table.Column<bool>(type: "bit", nullable: false),
                    Taxable1 = table.Column<bool>(type: "bit", nullable: false),
                    Taxable2 = table.Column<bool>(type: "bit", nullable: false),
                    IsCategory = table.Column<bool>(type: "bit", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FastKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FxRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => x.Id);
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
                name: "NumberCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FromTime = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    ToTime = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Sales = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
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
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    FileSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackagedThroughUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    SentThroughUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    DownloadCursor = table.Column<long>(type: "bigint", nullable: false),
                    LastSentAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Tax1Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Tax1Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    Tax2Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Tax2Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenderTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    OpensDrawer = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tombstones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MergedIntoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tombstones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportSourceRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    Entity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                name: "RoleGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Before = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    After = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                name: "CustomerOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Company = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SoldBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SoldById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<long>(type: "bigint", nullable: false),
                    CustomerNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(37)", maxLength: 37, nullable: false),
                    Size = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Paid = table.Column<bool>(type: "bit", nullable: false),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    ClosedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClosedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    InvoiceNumber = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CustomerNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Company = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    SalesPerson = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SalesPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastPaidDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastPaidAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Penalty = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tax1Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    Tax2Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    ShipTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Points = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<long>(type: "bigint", nullable: true),
                    User = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    KeyHint = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    LastAddress = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    HubKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Transport = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: false),
                    SerialPort = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BaudRate = table.Column<int>(type: "int", nullable: false),
                    Address = table.Column<int>(type: "int", nullable: false),
                    Region = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    StartFrequency = table.Column<int>(type: "int", nullable: false),
                    EndFrequency = table.Column<int>(type: "int", nullable: false),
                    CustomStartKHz = table.Column<int>(type: "int", nullable: false),
                    CustomSpacing = table.Column<int>(type: "int", nullable: false),
                    CustomChannels = table.Column<int>(type: "int", nullable: false),
                    BeeperMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LinkProfile = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DrmEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AntennaDetectorDb = table.Column<int>(type: "int", nullable: false),
                    FastTid = table.Column<bool>(type: "bit", nullable: false),
                    RssiThresholdDbm = table.Column<int>(type: "int", nullable: false),
                    DedupSeconds = table.Column<int>(type: "int", nullable: false),
                    DwellRounds = table.Column<int>(type: "int", nullable: false),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SettingsAppliedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
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
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                name: "Stations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    UseUkDateFormat = table.Column<bool>(type: "bit", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    SuspendedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccessLevel = table.Column<int>(type: "int", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StaffCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PinHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    FailedSignIns = table.Column<int>(type: "int", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                name: "DrawerMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DrawerSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    User = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DrawerSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenderType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ForeignAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    GiftCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<long>(type: "bigint", nullable: true),
                    User = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    TenderType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AppliedToPenalty = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TakenBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TakenById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                name: "DocumentTexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Barcode = table.Column<string>(type: "varchar(14)", unicode: false, maxLength: 14, nullable: true),
                    ItemType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    PackQuantity = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    WeightUnit = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    WeightTolerance = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    WeightToleranceUnit = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    UnitsPerCase = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
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
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReceivedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<long>(type: "bigint", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SoldAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Staff = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    VoidedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VoidedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UncountedSetToZero = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    ToStoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ShippedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ShippedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ReceivedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PowerDbm = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    ProtectedKey = table.Column<string>(type: "varchar(1000)", unicode: false, maxLength: 1000, nullable: true),
                    EnrollmentCodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    EnrollmentExpiresAtUtc = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    EnrolledAtUtc = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    LastUploadAtUtc = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    LastDownloadAtUtc = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    DownloadCursor = table.Column<long>(type: "bigint", nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                name: "TimeClockEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClockIn = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    ClockOut = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    EditReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EditedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EditedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EditedAtUtc = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                name: "CommissionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Basis = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Heading = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    Units = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Gross = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    StatusDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierStockCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    CaseQuantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Epc = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    SoldAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    SoldOnInvoice = table.Column<long>(type: "bigint", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Value1 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Value2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Value3 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OnHand = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KitItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    ComponentPlu = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ComponentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    OnHandAfter = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    DocumentNumber = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    User = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReorderNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Commission = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Restock = table.Column<bool>(type: "bit", nullable: false),
                    GiftCardNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenderType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Tendered = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Change = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AuthCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ForeignAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockCountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Expected = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Counted = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Variance = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    VarianceCost = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CountedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    NotCounted = table.Column<bool>(type: "bit", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockCountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockCountLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Epc = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    SourceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Plu = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Barcode = table.Column<string>(type: "varchar(14)", unicode: false, maxLength: 14, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    Department = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ItemType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Taxable1 = table.Column<bool>(type: "bit", nullable: false),
                    Taxable2 = table.Column<bool>(type: "bit", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    QuantityShipped = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    DestinationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtDestination = table.Column<bool>(type: "bit", nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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
                name: "SaleLineTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Epc = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ChangeNumber = table.Column<byte[]>(type: "rowversion", nullable: false),
                    SyncEditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_AccountMappings_ChangeNumber",
                table: "AccountMappings",
                column: "ChangeNumber");

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
                name: "IX_AuditEntries_Action_AtUtc",
                table: "AuditEntries",
                columns: new[] { "Action", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_AtUtc",
                table: "AuditEntries",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ChangeNumber",
                table: "AuditEntries",
                column: "ChangeNumber");

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
                name: "IX_Categories_ChangeNumber",
                table: "Categories",
                column: "ChangeNumber");

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
                name: "IX_Clients_ChangeNumber",
                table: "Clients",
                column: "ChangeNumber");

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
                name: "IX_CommissionRules_ChangeNumber",
                table: "CommissionRules",
                column: "ChangeNumber");

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
                name: "IX_CustomerOrders_ChangeNumber",
                table: "CustomerOrders",
                column: "ChangeNumber");

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
                name: "IX_Departments_ChangeNumber",
                table: "Departments",
                column: "ChangeNumber");

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
                name: "IX_DocumentTexts_ChangeNumber",
                table: "DocumentTexts",
                column: "ChangeNumber");

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
                name: "IX_DrawerMovements_ChangeNumber",
                table: "DrawerMovements",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerMovements_DrawerSessionId",
                table: "DrawerMovements",
                column: "DrawerSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerMovements_UpdatedAtUtc",
                table: "DrawerMovements",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DrawerSessions_ChangeNumber",
                table: "DrawerSessions",
                column: "ChangeNumber");

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
                name: "IX_DrawerSessionTenders_ChangeNumber",
                table: "DrawerSessionTenders",
                column: "ChangeNumber");

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
                name: "IX_FastKeys_ChangeNumber",
                table: "FastKeys",
                column: "ChangeNumber");

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
                name: "IX_FxRates_ChangeNumber",
                table: "FxRates",
                column: "ChangeNumber");

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
                name: "IX_GiftCards_ChangeNumber",
                table: "GiftCards",
                column: "ChangeNumber");

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
                name: "IX_GiftCardTransactions_ChangeNumber",
                table: "GiftCardTransactions",
                column: "ChangeNumber");

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
                name: "IX_ImportSourceRows_Entity_EntityId",
                table: "ImportSourceRows",
                columns: new[] { "Entity", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportSourceRows_ImportBatchId",
                table: "ImportSourceRows",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_ChangeNumber",
                table: "InvoiceLines",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_UpdatedAtUtc",
                table: "InvoiceLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_ChangeNumber",
                table: "InvoicePayments",
                column: "ChangeNumber");

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
                name: "IX_Invoices_ChangeNumber",
                table: "Invoices",
                column: "ChangeNumber");

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
                name: "IX_ItemMatrixColumns_ChangeNumber",
                table: "ItemMatrixColumns",
                column: "ChangeNumber");

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
                name: "IX_ItemPeriodSales_ChangeNumber",
                table: "ItemPeriodSales",
                column: "ChangeNumber");

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
                name: "IX_ItemPictures_ChangeNumber",
                table: "ItemPictures",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPictures_UpdatedAtUtc",
                table: "ItemPictures",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CategoryId",
                table: "Items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ChangeNumber",
                table: "Items",
                column: "ChangeNumber");

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
                name: "IX_ItemSerials_ChangeNumber",
                table: "ItemSerials",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ItemSerials_ItemId_SerialNumber",
                table: "ItemSerials",
                columns: new[] { "ItemId", "SerialNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSerials_UpdatedAtUtc",
                table: "ItemSerials",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItemSuppliers_ChangeNumber",
                table: "ItemSuppliers",
                column: "ChangeNumber");

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
                name: "IX_ItemTags_ChangeNumber",
                table: "ItemTags",
                column: "ChangeNumber");

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
                name: "IX_ItemVariants_ChangeNumber",
                table: "ItemVariants",
                column: "ChangeNumber");

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
                name: "IX_KitComponents_ChangeNumber",
                table: "KitComponents",
                column: "ChangeNumber");

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
                name: "IX_Locations_ChangeNumber",
                table: "Locations",
                column: "ChangeNumber");

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
                name: "IX_LoyaltyTransactions_ChangeNumber",
                table: "LoyaltyTransactions",
                column: "ChangeNumber");

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
                name: "IX_NumberCounters_ChangeNumber",
                table: "NumberCounters",
                column: "ChangeNumber");

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
                name: "IX_PurchaseOrderLines_ChangeNumber",
                table: "PurchaseOrderLines",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId_Plu",
                table: "PurchaseOrderLines",
                columns: new[] { "PurchaseOrderId", "Plu" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_UpdatedAtUtc",
                table: "PurchaseOrderLines",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_ChangeNumber",
                table: "PurchaseOrders",
                column: "ChangeNumber");

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
                name: "IX_RfidAntennas_ChangeNumber",
                table: "RfidAntennas",
                column: "ChangeNumber");

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
                name: "IX_RfidHubLinks_ChangeNumber",
                table: "RfidHubLinks",
                column: "ChangeNumber");

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
                name: "IX_RfidReaders_ChangeNumber",
                table: "RfidReaders",
                column: "ChangeNumber");

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
                name: "IX_Rmas_ChangeNumber",
                table: "Rmas",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Rmas_StoreId",
                table: "Rmas",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Rmas_UpdatedAtUtc",
                table: "Rmas",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RoleGrants_ChangeNumber",
                table: "RoleGrants",
                column: "ChangeNumber");

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
                name: "IX_Roles_ChangeNumber",
                table: "Roles",
                column: "ChangeNumber");

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
                name: "IX_SaleLines_ChangeNumber",
                table: "SaleLines",
                column: "ChangeNumber");

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
                name: "IX_SaleLineTags_ChangeNumber",
                table: "SaleLineTags",
                column: "ChangeNumber");

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
                name: "IX_SalePayments_ChangeNumber",
                table: "SalePayments",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_SalePayments_SaleId",
                table: "SalePayments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SalePayments_UpdatedAtUtc",
                table: "SalePayments",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ChangeNumber",
                table: "Sales",
                column: "ChangeNumber");

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
                name: "IX_StaffPeriods_ChangeNumber",
                table: "StaffPeriods",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPeriods_UpdatedAtUtc",
                table: "StaffPeriods",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StationLinks_ChangeNumber",
                table: "StationLinks",
                column: "ChangeNumber");

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
                name: "IX_Stations_ChangeNumber",
                table: "Stations",
                column: "ChangeNumber");

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
                name: "IX_StationSettings_ChangeNumber",
                table: "StationSettings",
                column: "ChangeNumber");

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
                name: "IX_StockCountLines_ChangeNumber",
                table: "StockCountLines",
                column: "ChangeNumber");

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
                name: "IX_StockCounts_ChangeNumber",
                table: "StockCounts",
                column: "ChangeNumber");

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
                name: "IX_StockCountTags_ChangeNumber",
                table: "StockCountTags",
                column: "ChangeNumber");

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
                name: "IX_StockMovements_ChangeNumber",
                table: "StockMovements",
                column: "ChangeNumber");

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
                name: "IX_StockTransferLines_ChangeNumber",
                table: "StockTransferLines",
                column: "ChangeNumber");

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
                name: "IX_StockTransfers_ChangeNumber",
                table: "StockTransfers",
                column: "ChangeNumber");

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
                name: "IX_StoreImages_ChangeNumber",
                table: "StoreImages",
                column: "ChangeNumber");

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
                name: "IX_Stores_ChangeNumber",
                table: "Stores",
                column: "ChangeNumber");

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
                name: "IX_StoreSettingExtras_ChangeNumber",
                table: "StoreSettingExtras",
                column: "ChangeNumber");

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
                name: "IX_StoreSettings_ChangeNumber",
                table: "StoreSettings",
                column: "ChangeNumber");

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
                name: "IX_Suppliers_AccountNumber",
                table: "Suppliers",
                column: "AccountNumber",
                unique: true,
                filter: "[AccountNumber] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_ChangeNumber",
                table: "Suppliers",
                column: "ChangeNumber");

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
                name: "IX_SuspendedSales_ChangeNumber",
                table: "SuspendedSales",
                column: "ChangeNumber");

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
                name: "IX_SyncBatches_ChangeNumber",
                table: "SyncBatches",
                column: "ChangeNumber");

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
                name: "IX_SyncStates_ChangeNumber",
                table: "SyncStates",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_SyncStates_UpdatedAtUtc",
                table: "SyncStates",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TaxConfigurations_ChangeNumber",
                table: "TaxConfigurations",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_TaxConfigurations_UpdatedAtUtc",
                table: "TaxConfigurations",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TenderTypes_ChangeNumber",
                table: "TenderTypes",
                column: "ChangeNumber");

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
                name: "IX_TimeClockEntries_ChangeNumber",
                table: "TimeClockEntries",
                column: "ChangeNumber");

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
                name: "IX_Tombstones_ChangeNumber",
                table: "Tombstones",
                column: "ChangeNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Tombstones_EntityName_RowId",
                table: "Tombstones",
                columns: new[] { "EntityName", "RowId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tombstones_UpdatedAtUtc",
                table: "Tombstones",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_ChangeNumber",
                table: "UserAccounts",
                column: "ChangeNumber");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountMappings");

            migrationBuilder.DropTable(
                name: "AuditEntries");

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
                name: "FastKeys");

            migrationBuilder.DropTable(
                name: "FxRates");

            migrationBuilder.DropTable(
                name: "GiftCardTransactions");

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
                name: "LoyaltyTransactions");

            migrationBuilder.DropTable(
                name: "NumberCounters");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "RfidAntennas");

            migrationBuilder.DropTable(
                name: "RfidHubLinks");

            migrationBuilder.DropTable(
                name: "Rmas");

            migrationBuilder.DropTable(
                name: "RoleGrants");

            migrationBuilder.DropTable(
                name: "SaleLineTags");

            migrationBuilder.DropTable(
                name: "SalePayments");

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
                name: "StoreImages");

            migrationBuilder.DropTable(
                name: "StoreSettingExtras");

            migrationBuilder.DropTable(
                name: "StoreSettings");

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
                name: "DrawerSessions");

            migrationBuilder.DropTable(
                name: "GiftCards");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "RfidReaders");

            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "Stations");

            migrationBuilder.DropTable(
                name: "StockCounts");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "StockTransfers");

            migrationBuilder.DropTable(
                name: "UserAccounts");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Stores");
        }
    }
}
