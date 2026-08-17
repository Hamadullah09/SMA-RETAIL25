using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShoppersAndTrolleys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shoppers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    normalized_email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    email_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    last_signed_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shoppers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trolley_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    trolley_id = table.Column<long>(type: "bigint", nullable: false),
                    shopper_id = table.Column<long>(type: "bigint", nullable: false),
                    cart_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    sale_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trolley_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trolleys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: false),
                    label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trolleys", x => x.id);
                    table.ForeignKey(
                        name: "fk_trolleys_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shopper_devices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    shopper_id = table.Column<long>(type: "bigint", nullable: false),
                    device_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    device_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    refresh_token_hash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    refresh_token_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    biometric_enabled = table.Column<bool>(type: "bit", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shopper_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_shopper_devices_shoppers_shopper_id",
                        column: x => x.shopper_id,
                        principalTable: "shoppers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shopper_devices_refresh_token_hash",
                table: "shopper_devices",
                column: "refresh_token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_shopper_devices_shopper_id_device_id",
                table: "shopper_devices",
                columns: new[] { "shopper_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shoppers_normalized_email",
                table: "shoppers",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shoppers_phone",
                table: "shoppers",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ix_trolley_sessions_cart_id",
                table: "trolley_sessions",
                column: "cart_id");

            migrationBuilder.CreateIndex(
                name: "ix_trolley_sessions_shopper_id_state",
                table: "trolley_sessions",
                columns: new[] { "shopper_id", "state" },
                unique: true,
                filter: "[state] = 'Shopping'");

            migrationBuilder.CreateIndex(
                name: "ix_trolley_sessions_state_last_activity_at",
                table: "trolley_sessions",
                columns: new[] { "state", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trolley_sessions_trolley_id_state",
                table: "trolley_sessions",
                columns: new[] { "trolley_id", "state" },
                unique: true,
                filter: "[state] = 'Shopping'");

            migrationBuilder.CreateIndex(
                name: "ix_trolleys_location_id_code",
                table: "trolleys",
                columns: new[] { "location_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trolleys_station_id",
                table: "trolleys",
                column: "station_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shopper_devices");

            migrationBuilder.DropTable(
                name: "trolley_sessions");

            migrationBuilder.DropTable(
                name: "trolleys");

            migrationBuilder.DropTable(
                name: "shoppers");
        }
    }
}
