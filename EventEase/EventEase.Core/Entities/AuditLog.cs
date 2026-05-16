using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Core.Entities
{
    public class AuditLog
    {
        public Guid Id { get; set; }
        public Guid AdminId { get; set; }
        public string Action { get; set; }
        public string Target { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
