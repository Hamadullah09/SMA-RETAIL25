using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HubTicketRedemptionCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "redemptions_remaining",
                table: "cached_hub_ticket",
                type: "int",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "redemptions_remaining",
                table: "cached_hub_ticket");
        }
    }
}
