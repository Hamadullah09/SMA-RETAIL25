using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "migration_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    entity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rows_staged = table.Column<int>(type: "integer", nullable: false),
                    rows_deleted_in_source = table.Column<int>(type: "integer", nullable: false),
                    blocking_errors = table.Column<int>(type: "integer", nullable: false),
                    warnings = table.Column<int>(type: "integer", nullable: false),
                    rows_imported = table.Column<int>(type: "integer", nullable: false),
                    rows_skipped = table.Column<int>(type: "integer", nullable: false),
                    analysis_json = table.Column<string>(type: "jsonb", nullable: true),
                    validation_json = table.Column<string>(type: "jsonb", nullable: true),
                    reconciliation_json = table.Column<string>(type: "jsonb", nullable: true),
                    validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dry_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_migration_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "migration_staging_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_deleted_in_source = table.Column<bool>(type: "boolean", nullable: false),
                    legacy_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    is_valid = table.Column<bool>(type: "boolean", nullable: true),
                    problems = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    outcome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_migration_staging_rows", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_migration_batches_location_id_stage",
                table: "migration_batches",
                columns: new[] { "location_id", "stage" });

            migrationBuilder.CreateIndex(
                name: "ix_migration_batches_source_hash",
                table: "migration_batches",
                column: "source_hash");

            migrationBuilder.CreateIndex(
                name: "ix_migration_staging_rows_batch_id_legacy_key",
                table: "migration_staging_rows",
                columns: new[] { "batch_id", "legacy_key" });

            migrationBuilder.CreateIndex(
                name: "ix_migration_staging_rows_batch_id_row_number",
                table: "migration_staging_rows",
                columns: new[] { "batch_id", "row_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "migration_batches");

            migrationBuilder.DropTable(
                name: "migration_staging_rows");
        }
    }
}
