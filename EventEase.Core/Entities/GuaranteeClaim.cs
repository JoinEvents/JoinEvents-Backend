using System;

namespace EventEase.Core.Entities
{
    /// <summary>
    /// A claim raised against the booking protection scheme.
    /// </summary>
    /// <remarks>
    /// These used to live in a static <c>ConcurrentBag</c> on the controller,
    /// so every claim vanished when the process restarted and was invisible to
    /// any other instance. The flag written onto the booking survived, which
    /// left customers with a booking marked "claimed" and no claim behind it.
    /// </remarks>
    public class GuaranteeClaim
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid VendorId { get; set; }

        /// <summary>no_show, quality or cancellation.</summary>
        public string ClaimType { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Evidence URLs, stored as JSON because the set is small, read whole,
        /// and never queried across.
        /// </summary>
        public string EvidenceJson { get; set; } = "[]";

        /// <summary>submitted, under_review, approved, rejected or resolved.</summary>
        public string Status { get; set; } = "submitted";

        public decimal? RefundAmount { get; set; }
        public decimal? CompensationAmount { get; set; }

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
        public string? Resolution { get; set; }

        public Booking? Booking { get; set; }
    }
}
