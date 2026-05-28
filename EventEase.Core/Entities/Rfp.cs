using System;

namespace EventEase.Core.Entities
{
    public class Rfp
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public string City { get; set; } = string.Empty;
        public int GuestCount { get; set; }
        public decimal BudgetMin { get; set; }
        public decimal BudgetMax { get; set; }
        public string Requirements { get; set; } = string.Empty;
        public string Status { get; set; } = "open"; // open, bid_selected, closed
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? ServicesNeededJson { get; set; } // list of services needed stored as JSON array
    }
}
