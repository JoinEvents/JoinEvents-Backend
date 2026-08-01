using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventEase.Core.Constants;
using EventEase.Core.Enums;

namespace EventEase.Core.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = AuthRoles.User; // User, Vendor, Admin
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

        // Moderation Settings
        public string AccountStatus { get; set; } = Enums.AccountStatus.Active.ToString().ToLowerInvariant(); // active, warning, restricted, suspended, banned
        public int Strikes { get; set; } = 0;
        public string? SuspensionReason { get; set; }
        public string? SuspensionDuration { get; set; }

        // Employee Settings
        public string? EmployeeId { get; set; }
        public string? Department { get; set; }
        public string? Designation { get; set; }
        public string? Shift { get; set; }
        public int TicketsResolved { get; set; } = 0;
        public int PerformanceScore { get; set; } = 0;
        public DateTime? LastLogin { get; set; }
    }
}
