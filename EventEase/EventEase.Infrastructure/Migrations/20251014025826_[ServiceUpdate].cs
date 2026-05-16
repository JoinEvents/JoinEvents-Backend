using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventEase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServiceUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Availability",
                table: "Services",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "Created",
                table: "Services",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Services",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MediaURL",
                table: "Services",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubCategory",
                table: "Services",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Availability",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "Created",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "MediaURL",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "SubCategory",
                table: "Services");
        }
    }
}
