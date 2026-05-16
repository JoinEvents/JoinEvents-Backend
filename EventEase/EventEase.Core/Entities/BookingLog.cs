using System;

namespace EventEase.Core.Entities
{
    public class BookingLog
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Actor { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
