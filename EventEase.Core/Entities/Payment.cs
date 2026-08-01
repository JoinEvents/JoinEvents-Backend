using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventEase.Core.Enums;

namespace EventEase.Core.Entities
{
    public class Payment
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = PaymentStatus.Initiated.ToString(); // Initiated, Succeeded, Failed, Refunded
        public string Provider { get; set; } = "Simulator";
        public string? ProviderReference { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
