using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Popular service names, stored as a JSON array.
        /// e.g. ["Venue","Catering","Decoration"]
        /// </summary>
        public string PopularServicesJson { get; set; } = "[]";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ── Helper (not mapped) ────────────────────────────────────────────

        /// <summary>
        /// The stored JSON as a list.
        ///
        /// A row whose column does not hold a JSON array — one written before this was JSON,
        /// or edited by hand — would otherwise throw out of a property getter, and the public
        /// category listing would answer 500 for every caller because of one bad row. A
        /// comma-separated value is read as the list it plainly is; anything else reads as
        /// empty, which shows a category without its service tags rather than no categories
        /// at all.
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public List<string> PopularServices
        {
            get => Parse(PopularServicesJson);
            set => PopularServicesJson = JsonSerializer.Serialize(value ?? new List<string>());
        }

        private static List<string> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();

            var trimmed = json.Trim();

            // Anything that opens like JSON is read as JSON or not at all: splitting a
            // malformed object on its commas would invent service names out of syntax.
            if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
            {
                try
                {
                    return JsonSerializer.Deserialize<List<string>>(trimmed) ?? new List<string>();
                }
                catch (JsonException)
                {
                    return new List<string>();
                }
            }

            return trimmed
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
    }
}
