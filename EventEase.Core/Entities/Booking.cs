using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventEase.Core.Enums;

namespace EventEase.Core.Entities
{
    public class Booking
    {
        public Guid Id { get; set; }
        public Guid? RfpId { get; set; } // Link back to enquiry
        public Guid UserId { get; set; }
        public Guid VendorId { get; set; }
        public DateTime EventDate { get; set; }
        public string Status { get; set; } = BookingStatus.Pending.ToString(); // Pending, Accepted, Rejected, Paid, Cancelled
        public decimal Amount { get; set; }
        public decimal TotalAmount { get; set; } = 0;
        public decimal AdvanceAmount { get; set; } = 0;
        public decimal DamageCharges { get; set; } = 0;
        public string? DamageChargeNotes { get; set; }
        public bool IsDamageChargeApproved { get; set; } = false;
        public decimal ExtraServicesAmount { get; set; } = 0;
        public decimal? FinalPaidAmount { get; set; }
        public string? CancelledBy { get; set; }
        public string? CancellationReason { get; set; }
        public DateTime? CancellationDate { get; set; }
        public decimal CancellationFee { get; set; } = 0;
        public decimal PlatformCancellationFeeRetained { get; set; } = 0;
        public decimal RefundAmount { get; set; } = 0;
        public string RefundStatus { get; set; } = Enums.RefundStatus.None.ToString().ToLowerInvariant(); // none, pending, processed, failed
        public string? RefundTransactionId { get; set; }
        public decimal VendorPenaltyAmount { get; set; } = 0;
        public bool VendorStrikeApplied { get; set; } = false;
        public Guid? PackageId { get; set; }
        public string? PackageName { get; set; }
        public string EventName { get; set; } = string.Empty;
        public string Venue { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public int GuestCount { get; set; }

        // Platform fee fields
        public decimal PlatformFeeRate { get; set; } = 0;
        public decimal PlatformFeeAmount { get; set; } = 0;
        public decimal TdsDeducted { get; set; } = 0;
        public decimal VendorPayoutAmount { get; set; } = 0;

        // Escrow and Guarantee fields
        public string EscrowStatus { get; set; } = Enums.EscrowStatus.Held.ToString().ToLowerInvariant(); // held, released, refunded
        public string GuaranteeStatus { get; set; } = Enums.GuaranteeStatus.Active.ToString().ToLowerInvariant(); // active, claimed, resolved, expired
        public DateTime? VendorConfirmedAt { get; set; }
        public DateTime? VendorConfirmationDue { get; set; }
    }
}
