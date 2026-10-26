using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPlus.Data.Migrations
{
    /// <inheritdoc />
    public partial class ItemWeightAndSaleLineTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Weight",
                table: "Items",
                type: "decimal(12,3)",
                precision: 12,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WeightUnit",
                table: "Items",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SaleLineTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaleLineId = table.Column<int>(type: "int", nullable: false),
                    Epc = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false)
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
                name: "IX_SaleLineTags_Epc",
                table: "SaleLineTags",
                column: "Epc");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLineTags_SaleLineId",
                table: "SaleLineTags",
                column: "SaleLineId");

            // Sales saved with one tag per line keep it.
            migrationBuilder.Sql("INSERT INTO SaleLineTags (SaleLineId, Epc) SELECT Id, Epc FROM SaleLines WHERE Epc IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "Epc",
                table: "SaleLines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Epc",
                table: "SaleLines",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("UPDATE l SET Epc = (SELECT MIN(t.Epc) FROM SaleLineTags t WHERE t.SaleLineId = l.Id) FROM SaleLines l");

            migrationBuilder.DropTable(
                name: "SaleLineTags");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "WeightUnit",
                table: "Items");
        }
    }
}
