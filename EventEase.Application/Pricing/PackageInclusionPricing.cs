using System.Text.Json;

namespace EventEase.Application.Pricing
{
    /// <summary>
    /// Reads the per-service prices a vendor enters when building a package.
    ///
    /// The vendor console stores them as JSON after a marker in the package description. Catering
    /// priced under <see cref="PerPlateThreshold"/> is a per-plate rate; every other service is a
    /// flat price for the event. Prices are entered before GST, which is added on top.
    /// </summary>
    public static class PackageInclusionPricing
    {
        public const string Marker = "---INCLUSION_DETAILS---";
        public const decimal GstRate = 0.18m;
        public const decimal PerPlateThreshold = 5000m;

        public record ServicePrice(string Name, decimal UnitPrice, bool PerPlate)
        {
            /// <summary>The price of this service for the given number of guests, before GST.</summary>
            public decimal AmountFor(int guests) => PerPlate ? UnitPrice * guests : UnitPrice;
        }

        private class InclusionDetail
        {
            public decimal MinPrice { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// The services priced in a package description, or null when the description carries no
        /// per-service prices (or they cannot be read).
        /// </summary>
        public static IReadOnlyList<ServicePrice>? Parse(string? description)
        {
            if (string.IsNullOrEmpty(description)) return null;

            var parts = description.Split(new[] { Marker }, StringSplitOptions.None);
            if (parts.Length < 2) return null;

            var json = parts[1].Trim();
            if (string.IsNullOrEmpty(json)) return null;

            Dictionary<string, InclusionDetail>? details;
            try
            {
                details = JsonSerializer.Deserialize<Dictionary<string, InclusionDetail>>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
            if (details is null || details.Count == 0) return null;

            return details
                .Select(kvp => new ServicePrice(
                    kvp.Key,
                    kvp.Value?.MinPrice ?? 0m,
                    kvp.Key.Contains("catering", StringComparison.OrdinalIgnoreCase) &&
                        (kvp.Value?.MinPrice ?? 0m) < PerPlateThreshold))
                .ToList();
        }

        /// <summary>The package's price for the given guest count, GST included.</summary>
        public static decimal GstInclusiveTotal(IEnumerable<ServicePrice> services, int guests)
        {
            var subtotal = services.Sum(s => s.AmountFor(guests));
            return Math.Round(subtotal * (1 + GstRate), 2);
        }
    }
}
