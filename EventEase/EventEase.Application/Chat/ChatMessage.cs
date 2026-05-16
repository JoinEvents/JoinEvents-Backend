using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Chat
{
    public class ChatMessage
    {
        public Guid Id { get; set; }
        public Guid SenderId { get; set; }
        public Guid ReceiverId { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
