using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CacheStoresInSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cached_cart",
                columns: table => new
                {
                    cart_id = table.Column<long>(type: "bigint", nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    saved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cached_cart", x => x.cart_id);
                });

            migrationBuilder.CreateTable(
                name: "cached_hub_ticket",
                columns: table => new
                {
                    ticket = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cached_hub_ticket", x => x.ticket);
                });

            migrationBuilder.CreateTable(
                name: "cached_idempotency_entry",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cached_idempotency_entry", x => x.idempotency_key);
                });

            migrationBuilder.CreateTable(
                name: "cached_tag_claim",
                columns: table => new
                {
                    epc = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cached_tag_claim", x => x.epc);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cached_cart_expires_at",
                table: "cached_cart",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_cached_cart_station_active",
                table: "cached_cart",
                columns: new[] { "station_id", "saved_at" },
                filter: "[is_active] = 1");

            migrationBuilder.CreateIndex(
                name: "ix_cached_hub_ticket_expires_at",
                table: "cached_hub_ticket",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_cached_idempotency_entry_expires_at",
                table: "cached_idempotency_entry",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_cached_tag_claim_expires_at",
                table: "cached_tag_claim",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cached_cart");

            migrationBuilder.DropTable(
                name: "cached_hub_ticket");

            migrationBuilder.DropTable(
                name: "cached_idempotency_entry");

            migrationBuilder.DropTable(
                name: "cached_tag_claim");
        }
    }
}
