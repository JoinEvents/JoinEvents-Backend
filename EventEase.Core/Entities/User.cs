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
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "User"; // User, Vendor, Admin
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? PasswordHash { get; set; }
        public string? City { get; set; }
        public string? Address { get; set; }
        public string? Bio { get; set; }
        public int LoyaltyPoints { get; set; } = 0;
        public string? LoyaltyTier { get; set; }
        
        // Referral System
        public string? ReferralCode { get; set; }
        public Guid? ReferredById { get; set; }

        // Notification Settings
        public bool EmailNotifications { get; set; } = true;
        public bool InAppNotifications { get; set; } = true;
        public bool SmsNotifications { get; set; } = false;

        // Profile Photo
        public string? Avatar { get; set; }
    }
}
