using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Core.Entities
{
    public class Booking
    {
        public Guid Id { get; set; }
        public Guid? RfpId { get; set; } // Link back to enquiry
        public Guid UserId { get; set; }
        public Guid VendorId { get; set; }
        public DateTime EventDate { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Accepted, Rejected, Paid, Cancelled
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
        public Guid? PackageId { get; set; }
        public string? PackageName { get; set; }
        public string EventName { get; set; } = "Event Celebration";
        public string Venue { get; set; } = "Hotel Banquet";
        public string City { get; set; } = "Mumbai";
        public int GuestCount { get; set; } = 100;
    }
}
