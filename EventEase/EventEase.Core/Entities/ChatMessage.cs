using System;

namespace EventEase.Core.Entities
{
    public class ChatMessage
    {
        public Guid Id { get; set; }
        public Guid ThreadId { get; set; }
        public Guid SenderId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
