using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// OpenIddict 5 to 7, alongside the move to net10.0.
    /// </summary>
    public partial class OpenIddictSevenAndNetTen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "OpenIddictTokens",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            // OpenIddict 6 renamed the logout endpoint to "end session", the words the OpenID
            // Connect spec uses — and it renamed the stored permission value with it, from
            // "ept:logout" to "ept:end_session".
            //
            // The seeder only creates clients it cannot find, never updates one that exists, so on
            // any database seeded before this upgrade the web client would keep a permission the new
            // server no longer recognises. Nothing fails at startup; sign-out fails later, per
            // client, with unauthorized_client — which is why the rename is carried here rather than
            // left to the seeder.
            migrationBuilder.Sql(
                """
                UPDATE OpenIddictApplications
                SET permissions = REPLACE(permissions, '"ept:logout"', '"ept:end_session"')
                WHERE permissions LIKE '%"ept:logout"%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE OpenIddictApplications
                SET permissions = REPLACE(permissions, '"ept:end_session"', '"ept:logout"')
                WHERE permissions LIKE '%"ept:end_session"%';
                """);

            // Narrowing back to 50 truncates any token type the wider column allowed. Rolling this
            // back is only safe on a database that has not issued tokens under OpenIddict 7.
            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "OpenIddictTokens",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150,
                oldNullable: true);
        }
    }
}
