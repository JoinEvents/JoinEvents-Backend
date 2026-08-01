using System;

namespace EventEase.Core.Entities
{
    public class BookingService
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = "pending"; // pending, InProgress, completed
        public decimal Price { get; set; } = 0;
    }
}
