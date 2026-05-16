using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventEase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class chatingAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RfpId",
                table: "ChatThreads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ChatThreads",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "RfpId",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RfpId",
                table: "ChatThreads");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ChatThreads");

            migrationBuilder.DropColumn(
                name: "RfpId",
                table: "Bookings");
        }
    }
}
