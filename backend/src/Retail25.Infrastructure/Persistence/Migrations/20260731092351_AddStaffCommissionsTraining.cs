using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffCommissionsTraining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "hours_worked",
                table: "time_clock_entries",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "value",
                table: "commission_rules",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "max_commission",
                table: "commission_rules",
                type: "numeric(19,2)",
                precision: 19,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "commission_type",
                table: "commission_rules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "commission_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_code_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    commission_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    commission_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate_applied = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    line_net = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    line_cost = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    was_capped = table.Column<bool>(type: "boolean", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commission_ledger_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_time_clock_entries_location_id_clock_in",
                table: "time_clock_entries",
                columns: new[] { "location_id", "clock_in" });

            migrationBuilder.CreateIndex(
                name: "ix_time_clock_entries_staff_id_clock_in",
                table: "time_clock_entries",
                columns: new[] { "staff_id", "clock_in" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_staff_id",
                table: "commission_rules",
                column: "staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_staff_id_product_id_department_id",
                table: "commission_rules",
                columns: new[] { "staff_id", "product_id", "department_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commission_ledger_entries_location_id_business_date",
                table: "commission_ledger_entries",
                columns: new[] { "location_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_ledger_entries_staff_id_business_date",
                table: "commission_ledger_entries",
                columns: new[] { "staff_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_ledger_entries_transaction_id",
                table: "commission_ledger_entries",
                column: "transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commission_ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_time_clock_entries_location_id_clock_in",
                table: "time_clock_entries");

            migrationBuilder.DropIndex(
                name: "ix_time_clock_entries_staff_id_clock_in",
                table: "time_clock_entries");

            migrationBuilder.DropIndex(
                name: "ix_commission_rules_staff_id",
                table: "commission_rules");

            migrationBuilder.DropIndex(
                name: "ix_commission_rules_staff_id_product_id_department_id",
                table: "commission_rules");

            migrationBuilder.AlterColumn<decimal>(
                name: "hours_worked",
                table: "time_clock_entries",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "value",
                table: "commission_rules",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "max_commission",
                table: "commission_rules",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,2)",
                oldPrecision: 19,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "commission_type",
                table: "commission_rules",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
