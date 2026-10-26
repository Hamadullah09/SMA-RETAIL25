using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPlus.Data.Migrations
{
    /// <inheritdoc />
    public partial class Rfid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Epc",
                table: "SaleLines",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Epc = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    SoldAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    SoldOnInvoice = table.Column<long>(type: "bigint", nullable: true)
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
                name: "RfidReaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidReaders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RfidAntennas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReaderId = table.Column<int>(type: "int", nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PowerDbm = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StationNumber = table.Column<int>(type: "int", nullable: true)
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
                name: "IX_RfidAntennas_ReaderId_Port",
                table: "RfidAntennas",
                columns: new[] { "ReaderId", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RfidAntennas_StationNumber",
                table: "RfidAntennas",
                column: "StationNumber");

            migrationBuilder.CreateIndex(
                name: "IX_RfidReaders_HubKey",
                table: "RfidReaders",
                column: "HubKey");

            migrationBuilder.CreateIndex(
                name: "IX_RfidReaders_Name",
                table: "RfidReaders",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemTags");

            migrationBuilder.DropTable(
                name: "RfidAntennas");

            migrationBuilder.DropTable(
                name: "RfidReaders");

            migrationBuilder.DropColumn(
                name: "Epc",
                table: "SaleLines");
        }
    }
}
