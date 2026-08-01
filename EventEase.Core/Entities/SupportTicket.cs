using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventEase.Core.Enums;

namespace EventEase.Core.Entities
{
    public class SupportTicket
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = SupportTicketStatus.Open.ToString(); // Open, InProgress, Resolved, Closed
        public string? EventName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public string? AttachmentUrl { get; set; }
        public Guid? BookingId { get; set; }
        public string Priority { get; set; } = SupportTicketPriority.Medium.ToString();
    }
}
