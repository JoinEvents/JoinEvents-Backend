using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventEase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTierGradientField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Gradient",
                table: "Tiers",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "linear-gradient(135deg,#6B7280,#374151)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Gradient",
                table: "Tiers");
        }
    }
}
