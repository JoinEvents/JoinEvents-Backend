using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Core.Entities
{
    public class Booking
    {
        public Guid Id { get; set; }
        public Guid? RfpId { get; set; } // Link back to enquiry
        public Guid UserId { get; set; }
        public Guid VendorId { get; set; }
        public DateTime EventDate { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Accepted, Rejected, Paid, Cancelled
        public decimal Amount { get; set; }
    }
}
