using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventEase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addInitialPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Spaces",
                table: "Packages");

            migrationBuilder.RenameColumn(
                name: "Pricing",
                table: "Packages",
                newName: "Pricing_Unit");

            migrationBuilder.RenameColumn(
                name: "Policies",
                table: "Packages",
                newName: "Policies_DjPolicy");

            migrationBuilder.RenameColumn(
                name: "Capacity",
                table: "Packages",
                newName: "Policies_DecorPolicy");

            migrationBuilder.RenameColumn(
                name: "Amenities",
                table: "Packages",
                newName: "Policies_CateringPolicy");

            migrationBuilder.RenameColumn(
                name: "Address",
                table: "Packages",
                newName: "Policies_AlcoholPolicy");

            migrationBuilder.AddColumn<string>(
                name: "Address_City",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Address_Country",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Address_Landmark",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Address_Locality",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Address_Pincode",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Address_State",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Address_Street",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Amenities_HasAc",
                table: "Packages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Amenities_HasChangingRooms",
                table: "Packages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Amenities_HasParking",
                table: "Packages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Amenities_HasPowerBackup",
                table: "Packages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Capacity_MaxGuests",
                table: "Packages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Capacity_ParkingCapacity",
                table: "Packages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Capacity_TotalRooms",
                table: "Packages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Pricing_BasePrice",
                table: "Packages",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Pricing_NonVegPrice",
                table: "Packages",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Pricing_Rent",
                table: "Packages",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Pricing_RoomPrice",
                table: "Packages",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Pricing_VegPrice",
                table: "Packages",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PackageSpace",
                columns: table => new
                {
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SeatingCapacity = table.Column<int>(type: "int", nullable: false),
                    FloatingCapacity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageSpace", x => new { x.PackageId, x.Id });
                    table.ForeignKey(
                        name: "FK_PackageSpace_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageSpace");

            migrationBuilder.DropColumn(
                name: "Address_City",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Address_Country",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Address_Landmark",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Address_Locality",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Address_Pincode",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Address_State",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Address_Street",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Amenities_HasAc",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Amenities_HasChangingRooms",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Amenities_HasParking",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Amenities_HasPowerBackup",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Capacity_MaxGuests",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Capacity_ParkingCapacity",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Capacity_TotalRooms",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Pricing_BasePrice",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Pricing_NonVegPrice",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Pricing_Rent",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Pricing_RoomPrice",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Pricing_VegPrice",
                table: "Packages");

            migrationBuilder.RenameColumn(
                name: "Pricing_Unit",
                table: "Packages",
                newName: "Pricing");

            migrationBuilder.RenameColumn(
                name: "Policies_DjPolicy",
                table: "Packages",
                newName: "Policies");

            migrationBuilder.RenameColumn(
                name: "Policies_DecorPolicy",
                table: "Packages",
                newName: "Capacity");

            migrationBuilder.RenameColumn(
                name: "Policies_CateringPolicy",
                table: "Packages",
                newName: "Amenities");

            migrationBuilder.RenameColumn(
                name: "Policies_AlcoholPolicy",
                table: "Packages",
                newName: "Address");

            migrationBuilder.AddColumn<string>(
                name: "Spaces",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
