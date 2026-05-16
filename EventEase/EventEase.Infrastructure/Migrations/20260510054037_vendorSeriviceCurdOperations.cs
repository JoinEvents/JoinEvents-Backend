using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventEase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class vendorSeriviceCurdOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BasePrice",
                table: "Packages");

            migrationBuilder.RenameColumn(
                name: "ItemsJson",
                table: "Packages",
                newName: "Spaces");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Amenities",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Capacity",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Packages",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Experience",
                table: "Packages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Includes",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Packages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "Packages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Policies",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Pricing",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "Rating",
                table: "Packages",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TotalReviews",
                table: "Packages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Packages",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "VendorId",
                table: "Packages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "PackageImages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsMain = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageImages_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageImages_PackageId",
                table: "PackageImages",
                column: "PackageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageImages");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Amenities",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Capacity",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Experience",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Includes",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Policies",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Pricing",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "TotalReviews",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "Packages");

            migrationBuilder.RenameColumn(
                name: "Spaces",
                table: "Packages",
                newName: "ItemsJson");

            migrationBuilder.AddColumn<decimal>(
                name: "BasePrice",
                table: "Packages",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
