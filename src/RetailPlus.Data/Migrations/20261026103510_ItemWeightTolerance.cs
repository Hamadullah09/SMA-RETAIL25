using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPlus.Data.Migrations
{
    /// <inheritdoc />
    public partial class ItemWeightTolerance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WeightTolerance",
                table: "Items",
                type: "decimal(12,3)",
                precision: 12,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WeightToleranceUnit",
                table: "Items",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeightTolerance",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "WeightToleranceUnit",
                table: "Items");
        }
    }
}
