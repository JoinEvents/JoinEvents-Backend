using System;
using System.Collections.Generic;

namespace EventEase.Core.Entities
{
    /// <summary>
    /// Represents a tier configuration within an EventCategory.
    /// e.g. "Premium", "Gold", "Silver", "Budget"
    /// </summary>
    public class Tier
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Display name of the Tier, e.g. "Premium", "Gold"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>FK to the EventCategory this tier applies to</summary>
        public Guid CategoryId { get; set; }

        /// <summary>Navigation property to the EventCategory</summary>
        public EventCategory Category { get; set; } = null!;

        /// <summary>Optional description of the Tier</summary>
        public string? Description { get; set; }

        /// <summary>Whether this tier configuration is active</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Configurable Bootstrap Icon class, e.g. "bi-gem", "bi-star-fill"</summary>
        public string Icon { get; set; } = "bi-layers";

        /// <summary>Configurable CSS linear gradient background for the Tier Card header</summary>
        public string Gradient { get; set; } = "linear-gradient(135deg,#6B7280,#374151)";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>List of service price ranges associated with this Tier</summary>
        public List<TierPriceRange> PriceRanges { get; set; } = new();
    }
}
