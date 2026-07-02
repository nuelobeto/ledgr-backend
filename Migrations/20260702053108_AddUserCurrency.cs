using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgrApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "currency",
                table: "AspNetUsers");
        }
    }
}
