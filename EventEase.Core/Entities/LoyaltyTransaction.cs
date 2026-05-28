using System;

namespace EventEase.Core.Entities
{
    public class LoyaltyTransaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        
        /// <summary>
        /// Points earned or redeemed. Always positive; the Type indicates if it's an addition or subtraction.
        /// </summary>
        public int Points { get; set; }
        
        /// <summary>
        /// "earned" or "redeemed"
        /// </summary>
        public string Type { get; set; } = string.Empty;
        
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public Guid? BookingId { get; set; }
        
        // Navigation properties
        public User? User { get; set; }
    }
}
