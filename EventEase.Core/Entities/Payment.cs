using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Core.Entities
{
    public class Payment
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "Initiated"; // Initiated, Succeeded, Failed, Refunded
        public string Provider { get; set; } = "Simulator";
        public string? ProviderReference { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
