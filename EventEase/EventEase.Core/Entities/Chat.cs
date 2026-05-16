using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Core.Entities
{
    public class Chat
    {
        public Guid Id { get; set; }
        public Guid SenderId { get; set; }
        public Guid ReceiverId { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
