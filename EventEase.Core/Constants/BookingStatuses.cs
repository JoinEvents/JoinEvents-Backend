using System;
using System.Collections.Generic;
using System.Linq;

namespace EventEase.Core.Constants
{
    /// <summary>
    /// Canonical booking status values and the transitions allowed between them.
    ///
    /// Status used to be assigned straight from the request body, which let a caller move their
    /// own booking to a paid or settled state without paying. Every change now goes through
    /// <see cref="CanTransition"/>.
    /// </summary>
    public static class BookingStatuses
    {
        public const string Pending = "Pending";
        public const string Accepted = "Accepted";
        public const string Rejected = "Rejected";
        public const string Paid = "Paid";
        public const string Confirmed = "Confirmed";
        public const string InProgress = "InProgress";
        public const string Completed = "Completed";
        public const string Settled = "Settled";
        public const string Cancelled = "Cancelled";
        public const string Disputed = "Disputed";

        /// <summary>
        /// Statuses that may only ever be reached through the payment flow. The status endpoint
        /// refuses to set these no matter who is calling.
        /// </summary>
        public static readonly IReadOnlySet<string> PaymentControlled =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Paid, Settled };

        private static readonly Dictionary<string, string[]> Allowed =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [Pending] = new[] { Accepted, Rejected, Cancelled },
                [Accepted] = new[] { Cancelled, Confirmed },
                [Paid] = new[] { Confirmed, Cancelled, Disputed },
                [Confirmed] = new[] { InProgress, Cancelled, Disputed },
                [InProgress] = new[] { Completed, Disputed },
                [Completed] = new[] { Disputed },
                [Settled] = new[] { Disputed },
                [Disputed] = new[] { Completed, Cancelled, Settled },
                [Rejected] = Array.Empty<string>(),
                [Cancelled] = Array.Empty<string>()
            };

        /// <summary>Returns the canonical spelling of a status, or null if it is not recognised.</summary>
        public static string? Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return null;

            return All.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static readonly string[] All =
        {
            Pending, Accepted, Rejected, Paid, Confirmed,
            InProgress, Completed, Settled, Cancelled, Disputed
        };

        /// <summary>True when <paramref name="to"/> is a legal next status after <paramref name="from"/>.</summary>
        public static bool CanTransition(string from, string to)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return false;

            return Allowed.TryGetValue(from, out var next)
                   && next.Any(s => s.Equals(to, StringComparison.OrdinalIgnoreCase));
        }
    }
}
