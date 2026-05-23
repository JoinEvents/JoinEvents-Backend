using System;

namespace EventEase.Core.Entities
{
    public class Review
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public Guid VendorId { get; set; }
        public Guid UserId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "published"; // published, flagged, removed
        public string? DisputeReason { get; set; }
    }
}
