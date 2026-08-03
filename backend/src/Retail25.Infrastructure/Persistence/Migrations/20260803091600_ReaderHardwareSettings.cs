using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReaderHardwareSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The defaults below are hand-set, not the ones the scaffolder produced.
            //
            // Left alone it writes 0 into every new integer column, and 0 is not a value any of
            // these enums defines: region 0 is no band at all, link profile 0 is no profile, and
            // device address 0 addresses the wrong unit on an RS-485 bus. An existing reader would
            // have come out of this migration configured with settings that cannot be applied, and
            // an empty power string means the power command is skipped entirely — a reader left on
            // whatever it happened to be running, silently.
            //
            // These are the same conservative defaults the domain declares: FCC over its full
            // channel window (7-57, which is 902.00-927.00 MHz), the vendor's recommended link
            // profile, the broadcast address, and full transmit power.
            migrationBuilder.AddColumn<int>(
                name: "antenna_return_loss_threshold_db",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "beeper",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "dense_reader_mode",
                table: "reader_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "device_address",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 255);

            migrationBuilder.AddColumn<int>(
                name: "frequency_end_index",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 57);

            migrationBuilder.AddColumn<int>(
                name: "frequency_start_index",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<bool>(
                name: "impinj_fast_tid",
                table: "reader_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "link_profile",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 209);

            migrationBuilder.AddColumn<string>(
                name: "output_power_dbm",
                table: "reader_profiles",
                type: "text",
                nullable: false,
                defaultValue: "30");

            migrationBuilder.AddColumn<int>(
                name: "region",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "antenna_return_loss_threshold_db",
                table: "reader_profiles");

            migrationBuilder.DropColumn(
                name: "beeper",
                table: "reader_profiles");

            migrationBuilder.DropColumn(
                name: "dense_reader_mode",
                table: "reader_profiles");

            migrationBuilder.DropColumn(
                name: "device_address",
                table: "reader_profiles");

            migrationBuilder.DropColumn(
                name: "frequency_end_index",
                table: "reader_profiles");

            migrationBuilder.DropColumn(
                name: "frequency_start_index",
                table: "reader_profiles");

            migrationBuilder.DropColumn(
                name: "impinj_fast_tid",
                table: "reader_profiles");

            migrationBuilder.DropColumn(
                name: "link_profile",
                table: "reader_profiles");

            migrationBuilder.DropColumn(
                name: "output_power_dbm",
                table: "reader_profiles");

            migrationBuilder.DropColumn(
                name: "region",
                table: "reader_profiles");
        }
    }
}
