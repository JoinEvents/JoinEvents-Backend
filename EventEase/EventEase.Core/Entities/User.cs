using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Core.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Role { get; set; } // User, Vendor, Admin
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? PasswordHash { get; set; }
        public string? City { get; set; }
        public string? Address { get; set; }
        public string? Bio { get; set; }
        public int LoyaltyPoints { get; set; } = 0;
        public string? LoyaltyTier { get; set; }
    }
}
