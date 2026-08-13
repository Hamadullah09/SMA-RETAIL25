using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail25.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefundLinkage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "refunds_sale_line_id",
                table: "sale_lines",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_refunds_sale_line_id",
                table: "sale_lines",
                column: "refunds_sale_line_id",
                filter: "[refunds_sale_line_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sale_lines_refunds_sale_line_id",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "refunds_sale_line_id",
                table: "sale_lines");
        }
    }
}
