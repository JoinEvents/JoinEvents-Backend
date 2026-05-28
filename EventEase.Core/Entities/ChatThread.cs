using System;

namespace EventEase.Core.Entities
{
    public class ChatThread
    {
        public Guid Id { get; set; }
        public Guid? RfpId { get; set; } // Link to the enquiry
        public Guid CustomerId { get; set; }
        public Guid VendorId { get; set; }
        public string? LastMessage { get; set; }
        public int UnreadCount { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Accepted, Rejected, Active, Closed
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
