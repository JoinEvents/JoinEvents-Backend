using System;
using EventEase.Core.Enums;

namespace EventEase.Core.Entities
{
    public class Rfp
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string EventTypeId { get; set; } = string.Empty;
        public string EventTypeName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public string City { get; set; } = string.Empty;
        public string VenueStatus { get; set; } = "not_booked"; // booked, not_booked
        public string? VenueName { get; set; }
        public string? Locality { get; set; }
        public string? Pincode { get; set; }
        public int GuestCount { get; set; }
        public decimal BudgetMin { get; set; }
        public decimal BudgetMax { get; set; }
        public string Requirements { get; set; } = string.Empty;
        public string Status { get; set; } = RfpStatus.Open.ToString().ToLowerInvariant(); // open, bid_selected, closed
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
        public string? ServicesNeededJson { get; set; } // list of services needed stored as JSON array
    }
}
