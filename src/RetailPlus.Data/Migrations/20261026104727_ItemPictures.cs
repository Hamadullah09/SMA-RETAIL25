using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPlus.Data.Migrations
{
    /// <inheritdoc />
    public partial class ItemPictures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemPictures",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPictures", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_ItemPictures_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemPictures");
        }
    }
}
