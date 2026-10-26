using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPlus.Data.Migrations
{
    /// <inheritdoc />
    public partial class ItemBarcode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Items",
                type: "varchar(14)",
                unicode: false,
                maxLength: 14,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_LocationId_Barcode",
                table: "Items",
                columns: new[] { "LocationId", "Barcode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_LocationId_Barcode",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "Items");
        }
    }
}
