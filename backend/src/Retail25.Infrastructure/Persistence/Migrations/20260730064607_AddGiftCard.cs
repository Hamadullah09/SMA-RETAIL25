using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gift_cards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "text", nullable: false),
                    original_value = table.Column<decimal>(type: "numeric", nullable: false),
                    remaining_value = table.Column<decimal>(type: "numeric", nullable: false),
                    issued_to_customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_cards", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gift_cards");
        }
    }
}
