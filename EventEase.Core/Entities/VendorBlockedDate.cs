using System;

namespace EventEase.Core.Entities
{
    public class VendorBlockedDate
    {
        public Guid Id { get; set; }
        public Guid VendorId { get; set; }
        public DateTime BlockedDate { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
