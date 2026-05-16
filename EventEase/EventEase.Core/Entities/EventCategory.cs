using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EventEase.Core.Entities
{
    /// <summary>
    /// Represents an event category that vendors use to classify their services.
    /// e.g. "Wedding" (key: "wedding"), "Birthday" (key: "birthday")
    /// </summary>
    public class EventCategory
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Display name shown to vendors and customers. e.g. "Wedding Photography"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional Hindi label. e.g. "Shaadi Tasveerein"</summary>
        public string? NameHindi { get; set; }

        /// <summary>
        /// Auto-generated unique slug key derived from Name.
        /// e.g. "wedding_photography"
        /// Must be lowercase, underscores only.
        /// </summary>
        public string CategoryKey { get; set; } = string.Empty;

        /// <summary>Bootstrap icon class. e.g. "bi-hearts"</summary>
        public string Icon { get; set; } = string.Empty;

        /// <summary>CSS gradient for card header. e.g. "linear-gradient(135deg,#E91E8C,#FF6B6B)"</summary>
        public string? Gradient { get; set; }

        /// <summary>Optional CSS utility class. e.g. "event-wedding"</summary>
        public string? ColorClass { get; set; }

        /// <summary>Lowest starting price hint shown to customers.</summary>
        public decimal? StartingPrice { get; set; }

        /// <summary>Short description of the category.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// Comma-separated popular service names stored as JSON array.
        /// e.g. ["Venue","Catering","Decoration"]
        /// </summary>
        public string PopularServicesJson { get; set; } = "[]";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ── Helper (not mapped) ────────────────────────────────────────────

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public List<string> PopularServices
        {
            get => string.IsNullOrEmpty(PopularServicesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(PopularServicesJson) ?? new List<string>();
            set => PopularServicesJson = JsonSerializer.Serialize(value ?? new List<string>());
        }
    }
}
