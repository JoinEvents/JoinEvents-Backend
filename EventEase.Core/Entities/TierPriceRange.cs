using System;

namespace EventEase.Core.Entities
{
    /// <summary>
    /// Represents the minimum and maximum price range for a specific incurred service type under a Tier.
    /// e.g. "Venue" -> min: 50,000, max: 1,00,000
    /// </summary>
    public class TierPriceRange
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>FK to the parent Tier</summary>
        public Guid TierId { get; set; }

        /// <summary>Navigation property to the Tier</summary>
        public Tier Tier { get; set; } = null!;

        /// <summary>Service category/name, e.g. "Venue", "Catering", "Decoration"</summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>Minimum price for this service type in this tier</summary>
        public decimal MinPrice { get; set; }

        /// <summary>Maximum price for this service type in this tier</summary>
        public decimal MaxPrice { get; set; }
    }
}
