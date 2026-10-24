using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Introduces pos.clear_sale and grants it to the manager roles of a database that already exists.
    /// <para>
    /// Data, not schema, and a migration rather than a seeder change because production never seeds:
    /// Database:Seed is off there, and the deploy ships its schema as the idempotent script this
    /// becomes part of. A permission added only to the seeder would exist on a developer's laptop and
    /// nowhere a till is — and with nobody holding it, clearing a sale would ask for a manager that no
    /// manager could approve.
    /// </para>
    /// <para>
    /// Granted by legacy level, not by role name, because names are editable and levels are what the
    /// presets are defined against: 3 is Supervisor and 4 Administrator. A role an administrator has
    /// since created is left alone; whether it should clear sales is their decision.
    /// </para>
    /// <para>
    /// Both statements are guarded so the script can run against a database the seeder has already
    /// brought up to date, and against a brand-new one where the roles do not exist yet — there the
    /// second statement matches nothing, and the seeder creates the roles with their full presets.
    /// </para>
    /// </summary>
    public partial class ManagerClearsTheSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM [permissions] WHERE [key] = N'pos.clear_sale')
                    INSERT INTO [permissions] ([key], [description], [group], [is_sensitive], [row_version])
                    VALUES (N'pos.clear_sale', N'Clear sale', N'Point of sale', 1, 0);
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO [role_permissions] ([role_id], [permission_key], [row_version])
                SELECT r.[id], N'pos.clear_sale', 0
                FROM [AspNetRoles] AS r
                WHERE r.[legacy_level] >= 3
                  AND NOT EXISTS (
                      SELECT 1 FROM [role_permissions] AS rp
                      WHERE rp.[role_id] = r.[id] AND rp.[permission_key] = N'pos.clear_sale');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [role_permissions] WHERE [permission_key] = N'pos.clear_sale';");
            migrationBuilder.Sql("DELETE FROM [permissions] WHERE [key] = N'pos.clear_sale';");
        }
    }
}
