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
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "frequency_end_index",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "frequency_start_index",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "output_power_dbm",
                table: "reader_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "region",
                table: "reader_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
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
