using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventEase.Infrastructure.Migrations
{
    /// <summary>
    /// Adds columns the EF model has had for some time and that no migration ever created.
    /// </summary>
    /// <remarks>
    /// The model and the database had drifted: Booking alone declared seventeen properties
    /// with no column behind them. Every query that materialises one of these entities asks
    /// SQL Server for columns it does not have, so it fails with "Invalid column name" and
    /// the endpoint answers 500 — which is what the admin dashboard, the vendor dashboard and
    /// anything else reading bookings were doing.
    ///
    /// Written by hand against EventEaseDbContextModelSnapshot, which already describes the
    /// intended shape, so this migration closes the gap rather than changing the design.
    /// Defaults match the entity defaults, so existing rows read the same as a newly
    /// inserted one rather than as nulls the code does not expect.
    /// </remarks>
    public partial class AddMissingBookingAndTicketColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DamageChargeNotes",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DamageCharges",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "EscrowStatus",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExtraServicesAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalPaidAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuaranteeStatus",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDamageChargeApproved",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformFeeAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformFeeRate",
                table: "Bookings",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TdsDeducted",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "VendorConfirmationDue",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VendorConfirmedAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VendorPayoutAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsInternal",
                table: "ChatMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "SupportTickets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BookingId",
                table: "SupportTickets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "SupportTickets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdvanceAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DamageChargeNotes",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DamageCharges",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "EscrowStatus",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ExtraServicesAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "FinalPaidAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "GuaranteeStatus",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IsDamageChargeApproved",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PlatformFeeAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PlatformFeeRate",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TdsDeducted",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TotalAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VendorConfirmationDue",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VendorConfirmedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "VendorPayoutAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IsInternal",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "BookingId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "SupportTickets");
        }
    }
}
