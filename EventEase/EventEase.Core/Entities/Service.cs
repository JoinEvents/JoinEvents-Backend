using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Core.Entities
{
    public class Service
    {
        public Guid Id { get; set; }
        public Guid VendorId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } // e.g., Catering, Decor
        public string SubCategory { get; set; } = string.Empty;
        public decimal Price { get; set; } = 0;
        public string Availability { get; set; } = string.Empty;
        public string MediaURL { get; set; } = string.Empty;
        public DateTime Created { get; set; } = DateTime.Now;
        public double Rating { get; set; } = 0.0;
        public int  Status { get; set; }  // Active, Inactive

        public Vendor vendors { get; set; }
    }
}
