using System;
using EventEase.Core.Enums;

namespace EventEase.Core.Entities
{
    public class Bid
    {
        public Guid Id { get; set; }
        public Guid RfpId { get; set; }
        public Guid VendorId { get; set; }
        public decimal ProposedAmount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? DeliverablesJson { get; set; } // list of deliverables as JSON array
        public DateTime ValidUntil { get; set; }
        public string Status { get; set; } = BidStatus.Pending.ToString().ToLowerInvariant(); // pending, accepted, rejected
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    }
}
