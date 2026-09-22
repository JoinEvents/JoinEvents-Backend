using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventEase.Infrastructure.Migrations
{
    /// <summary>
    /// Creates the table that booking protection claims are stored in.
    /// </summary>
    /// <remarks>
    /// Claims used to live in a static collection on the controller, so every
    /// one was lost when the process restarted and none were visible to any
    /// other instance — while the "claimed" flag written onto the booking
    /// survived, leaving customers with a booking marked as claimed and no
    /// claim behind it.
    ///
    /// Written by hand, and deliberately narrower than what `ef migrations add`
    /// produced. The scaffolder also swept in pre-existing drift between the
    /// model and the database — unique indexes on Users.Email and
    /// Payments.ProviderReference, and a dozen other indexes no migration ever
    /// created. Those are not this change's to make: a unique index fails
    /// outright if the live data already holds a duplicate, and bundling that
    /// into a feature migration would take the API down on deploy with no
    /// obvious cause. The drift is real and worth closing on its own, with a
    /// look at the production data first.
    /// </remarks>
    public partial class AddGuaranteeClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuaranteeClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CompensationAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuaranteeClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuaranteeClaims_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // A claim is looked up by its booking, or by either side of it.
            migrationBuilder.CreateIndex(
                name: "IX_GuaranteeClaims_BookingId",
                table: "GuaranteeClaims",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_GuaranteeClaims_CustomerId",
                table: "GuaranteeClaims",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_GuaranteeClaims_VendorId",
                table: "GuaranteeClaims",
                column: "VendorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuaranteeClaims");
        }
    }
}
