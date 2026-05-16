using System;

namespace EventEase.Core.Entities
{
    public class Notification
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = "general"; // booking, rfp, general, support
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
