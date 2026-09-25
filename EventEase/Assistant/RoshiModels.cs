using System.Text.Json.Serialization;

namespace EventEase.Api.Assistant
{
    /// <summary>One turn of the conversation as the apps send it: role "user" or "assistant".</summary>
    public record RoshiTurn(string? Role, string? Content);

    public record RoshiRequest(List<RoshiTurn>? Messages);

    /// <summary>
    /// Roshi's answer. The text is plain with light markdown (**bold**, "- " bullets); cards carry
    /// the customer's real packages, bookings or rewards for the apps to render, and actions name
    /// a screen by meaning ("bookings", "package:{id}") so each app maps it to its own route.
    /// </summary>
    public class RoshiReply
    {
        [JsonPropertyName("reply")] public string Reply { get; set; } = "";
        [JsonPropertyName("cards")] public List<RoshiCard> Cards { get; set; } = new();
        [JsonPropertyName("actions")] public List<RoshiAction> Actions { get; set; } = new();
        [JsonPropertyName("suggestions")] public List<string> Suggestions { get; set; } = new();
        /// <summary>"claude" when the language model answered, "rules" for the built-in engine.</summary>
        [JsonPropertyName("poweredBy")] public string PoweredBy { get; set; } = "rules";
    }

    public class RoshiCard
    {
        /// <summary>"packages", "bookings" or "rewards".</summary>
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("packages")] public List<PackageCard>? Packages { get; set; }
        [JsonPropertyName("bookings")] public List<BookingCard>? Bookings { get; set; }
        [JsonPropertyName("rewards")] public RewardsCard? Rewards { get; set; }
    }

    public record RoshiAction(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("target")] string Target);

    public class PackageCard
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("vendorName")] public string VendorName { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("city")] public string City { get; set; } = "";
        [JsonPropertyName("price")] public decimal Price { get; set; }
        [JsonPropertyName("maxGuests")] public int MaxGuests { get; set; }
        [JsonPropertyName("rating")] public double Rating { get; set; }
        [JsonPropertyName("reviews")] public int Reviews { get; set; }
        [JsonPropertyName("image")] public string? Image { get; set; }
    }

    public class BookingCard
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("bookingNumber")] public string BookingNumber { get; set; } = "";
        [JsonPropertyName("eventName")] public string EventName { get; set; } = "";
        [JsonPropertyName("eventDate")] public string EventDate { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("vendorName")] public string VendorName { get; set; } = "";
        [JsonPropertyName("packageName")] public string PackageName { get; set; } = "";
        [JsonPropertyName("guests")] public int Guests { get; set; }
        [JsonPropertyName("totalAmount")] public decimal TotalAmount { get; set; }
        [JsonPropertyName("amountPaid")] public decimal AmountPaid { get; set; }
        [JsonPropertyName("balanceDue")] public decimal BalanceDue { get; set; }
        /// <summary>The advance on record, which the refund schedule is based on; not sent to the apps.</summary>
        [JsonIgnore] public decimal AdvanceAmount { get; set; }
        [JsonIgnore] public DateTime EventDay { get; set; }
    }

    public class RewardsCard
    {
        [JsonPropertyName("points")] public int Points { get; set; }
        [JsonPropertyName("tier")] public string Tier { get; set; } = "Bronze";
        [JsonPropertyName("nextTier")] public string? NextTier { get; set; }
        [JsonPropertyName("pointsToNextTier")] public int? PointsToNextTier { get; set; }
    }
}
