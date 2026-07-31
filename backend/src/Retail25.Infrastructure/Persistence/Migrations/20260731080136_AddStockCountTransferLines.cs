using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockCountTransferLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "stock_transfers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "stock_transfers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "received_at",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shipped_at",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "transfer_number",
                table: "stock_transfers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "stock_counts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "stock_counts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "count_number",
                table: "stock_counts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "department_id",
                table: "stock_counts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "posted_at",
                table: "stock_counts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stock_count_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    counted_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    system_qty_at_count = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_count_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_received = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_from_location_id_status",
                table: "stock_transfers",
                columns: new[] { "from_location_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_from_location_id_transfer_number",
                table: "stock_transfers",
                columns: new[] { "from_location_id", "transfer_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_to_location_id_status",
                table: "stock_transfers",
                columns: new[] { "to_location_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_location_id_count_number",
                table: "stock_counts",
                columns: new[] { "location_id", "count_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_location_id_status",
                table: "stock_counts",
                columns: new[] { "location_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_count_lines_stock_count_id",
                table: "stock_count_lines",
                column: "stock_count_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_count_lines_stock_count_id_product_id_variant_id",
                table: "stock_count_lines",
                columns: new[] { "stock_count_id", "product_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_stock_transfer_id",
                table: "stock_transfer_lines",
                column: "stock_transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_stock_transfer_id_product_id_variant_id",
                table: "stock_transfer_lines",
                columns: new[] { "stock_transfer_id", "product_id", "variant_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_count_lines");

            migrationBuilder.DropTable(
                name: "stock_transfer_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_from_location_id_status",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_from_location_id_transfer_number",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_to_location_id_status",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_counts_location_id_count_number",
                table: "stock_counts");

            migrationBuilder.DropIndex(
                name: "ix_stock_counts_location_id_status",
                table: "stock_counts");

            migrationBuilder.DropColumn(
                name: "received_at",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "shipped_at",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "transfer_number",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "count_number",
                table: "stock_counts");

            migrationBuilder.DropColumn(
                name: "department_id",
                table: "stock_counts");

            migrationBuilder.DropColumn(
                name: "posted_at",
                table: "stock_counts");

            migrationBuilder.AlterColumn<int>(
                name: "status",
                table: "stock_transfers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "stock_transfers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "status",
                table: "stock_counts",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "stock_counts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
