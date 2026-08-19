using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RfidTopology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    device_key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    hostname = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    local_ip_addresses = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    operating_system = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    agent_version = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    last_heartbeat = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rfid_readers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    reader_key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    serial_number = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    device_id = table.Column<long>(type: "bigint", nullable: true),
                    host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    port = table.Column<int>(type: "int", nullable: false),
                    protocol = table.Column<int>(type: "int", nullable: false),
                    antenna_count = table.Column<int>(type: "int", nullable: false),
                    last_seen = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfid_readers", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfid_readers_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reader_antenna_assignments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    reader_id = table.Column<long>(type: "bigint", nullable: false),
                    antenna_number = table.Column<int>(type: "int", nullable: false),
                    station_id = table.Column<long>(type: "bigint", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reader_antenna_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_reader_antenna_assignments_rfid_readers_reader_id",
                        column: x => x.reader_id,
                        principalTable: "rfid_readers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reader_antenna_assignments_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_devices_location_id_device_key",
                table: "devices",
                columns: new[] { "location_id", "device_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reader_antenna_assignments_reader_id",
                table: "reader_antenna_assignments",
                column: "reader_id");

            migrationBuilder.CreateIndex(
                name: "ix_reader_antenna_assignments_reader_id_antenna_number",
                table: "reader_antenna_assignments",
                columns: new[] { "reader_id", "antenna_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reader_antenna_assignments_station_id",
                table: "reader_antenna_assignments",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfid_readers_device_id",
                table: "rfid_readers",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfid_readers_location_id_reader_key",
                table: "rfid_readers",
                columns: new[] { "location_id", "reader_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rfid_readers_serial_number",
                table: "rfid_readers",
                column: "serial_number",
                unique: true,
                filter: "[serial_number] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reader_antenna_assignments");

            migrationBuilder.DropTable(
                name: "rfid_readers");

            migrationBuilder.DropTable(
                name: "devices");
        }
    }
}
