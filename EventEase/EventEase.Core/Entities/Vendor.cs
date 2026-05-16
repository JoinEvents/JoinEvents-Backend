using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Core.Entities
{
    public class Vendor
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string BusinessName { get; set; }
        public string Description { get; set; }
        public string Location { get; set; }
        public bool IsValidated { get; set; }
        public string? DocumentsJson { get; set; } // metadata of uploaded docs
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<Service> services { get; set; }

    }
}
