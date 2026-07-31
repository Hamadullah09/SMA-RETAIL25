using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalYearClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fiscal_years",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_rows = table.Column<int>(type: "integer", nullable: false),
                    archived_net_sales = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_years", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_history_archives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_code_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_sold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    net_sales = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    cost_of_goods_sold = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    transaction_count = table.Column<int>(type: "integer", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_history_archives", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_years_location_id_starts_on",
                table: "fiscal_years",
                columns: new[] { "location_id", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_years_location_id_year",
                table: "fiscal_years",
                columns: new[] { "location_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_history_archives_fiscal_year_id",
                table: "sales_history_archives",
                column: "fiscal_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_history_archives_location_id_year_month_product_id",
                table: "sales_history_archives",
                columns: new[] { "location_id", "year", "month", "product_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_years");

            migrationBuilder.DropTable(
                name: "sales_history_archives");
        }
    }
}
