using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventEase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRfpFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventTypeId",
                table: "Rfps",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EventTypeName",
                table: "Rfps",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Rfps",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Locality",
                table: "Rfps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pincode",
                table: "Rfps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueName",
                table: "Rfps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueStatus",
                table: "Rfps",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventTypeId",
                table: "Rfps");

            migrationBuilder.DropColumn(
                name: "EventTypeName",
                table: "Rfps");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Rfps");

            migrationBuilder.DropColumn(
                name: "Locality",
                table: "Rfps");

            migrationBuilder.DropColumn(
                name: "Pincode",
                table: "Rfps");

            migrationBuilder.DropColumn(
                name: "VenueName",
                table: "Rfps");

            migrationBuilder.DropColumn(
                name: "VenueStatus",
                table: "Rfps");
        }
    }
}
