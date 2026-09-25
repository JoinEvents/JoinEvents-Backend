using EventEase.Application.Loyalty;
using EventEase.Core.Constants;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Api.Assistant
{
    /// <summary>
    /// What Roshi can look up, for one signed-in customer. Everything is read-only and scoped to
    /// that customer: the assistant can never see or change another person's bookings.
    /// </summary>
    public class RoshiTools
    {
        private readonly EventEaseDbContext _db;
        private readonly ILoyaltyService _loyalty;
        private readonly decimal _advanceRate;

        public RoshiTools(EventEaseDbContext db, ILoyaltyService loyalty, IConfiguration config)
        {
            _db = db;
            _loyalty = loyalty;
            _advanceRate = config.GetValue("Pricing:AdvanceRate", 0.30m);
        }

        public int AdvancePercent => (int)Math.Round(_advanceRate * 100);

        /// <summary>Listed, verified packages matching the filters; best rated first.</summary>
        public async Task<List<PackageCard>> SearchPackagesAsync(
            string? category, string? city, decimal? maxBudget, int? guests, string? text, int take = 4)
        {
            var query = _db.Packages.AsNoTracking().Where(p => p.IsActive && p.IsVerified);
            if (!string.IsNullOrWhiteSpace(category))
            {
                var c = category.Trim().ToLower();
                query = query.Where(p => p.Category.ToLower().Contains(c));
            }
            if (!string.IsNullOrWhiteSpace(city))
            {
                var c = city.Trim().ToLower();
                query = query.Where(p => p.Address.City.ToLower().Contains(c) || p.Address.Locality.ToLower().Contains(c));
            }
            if (maxBudget is > 0) query = query.Where(p => p.Pricing.BasePrice <= maxBudget.Value);
            if (guests is > 0) query = query.Where(p => p.Capacity.MaxGuests == null || p.Capacity.MaxGuests == 0 || p.Capacity.MaxGuests >= guests.Value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var t = text.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(t) || p.Theme.ToLower().Contains(t));
            }

            // Rating is ordered in SQL; the price tie-break is done in memory (SQLite can't order decimals).
            var candidates = await query
                .OrderByDescending(p => p.Rating)
                .Take(40)
                .Select(p => new
                {
                    p.Id, p.Name, p.VendorId, p.Category, p.Address.City, p.Address.Locality,
                    Price = p.Pricing.BasePrice, p.Capacity.MaxGuests, p.Rating, p.TotalReviews,
                    Image = p.Images.OrderByDescending(i => i.IsMain).Select(i => i.Url).FirstOrDefault()
                })
                .ToListAsync();
            var packages = candidates
                .OrderByDescending(p => p.Rating).ThenBy(p => p.Price ?? decimal.MaxValue)
                .Take(Math.Clamp(take, 1, 8))
                .ToList();

            // A package's VendorId is the vendor profile id (older rows used the vendor's user id).
            var vendorIds = packages.Select(p => p.VendorId).Distinct().ToList();
            var vendors = await _db.Vendors.AsNoTracking()
                .Where(v => vendorIds.Contains(v.Id) || vendorIds.Contains(v.UserId))
                .Select(v => new { v.Id, v.UserId, v.BusinessName })
                .ToListAsync();
            string VendorName(Guid id) =>
                vendors.FirstOrDefault(v => v.Id == id || v.UserId == id)?.BusinessName ?? "";

            return packages.Select(p => new PackageCard
            {
                Id = $"pkg_{p.Id:N}",
                Name = p.Name,
                VendorName = VendorName(p.VendorId),
                Category = p.Category,
                City = string.IsNullOrWhiteSpace(p.City) ? p.Locality : p.City,
                Price = p.Price ?? 0m,
                MaxGuests = p.MaxGuests ?? 0,
                Rating = Math.Round(p.Rating, 1),
                Reviews = p.TotalReviews,
                Image = p.Image
            }).ToList();
        }

        /// <summary>The customer's bookings, soonest event first, with what is paid and still due.</summary>
        public async Task<List<BookingCard>> MyBookingsAsync(Guid userId)
        {
            var bookings = await _db.Bookings.AsNoTracking()
                .Where(b => b.UserId == userId)
                .OrderBy(b => b.EventDate)
                .Take(50)
                .ToListAsync();
            if (bookings.Count == 0) return new();

            var ids = bookings.Select(b => b.Id).ToList();
            // Summed in memory, as the bookings endpoint does: SQLite and SQL Server 2014 differ on decimal sums.
            var paid = (await _db.Payments.AsNoTracking()
                    .Where(p => ids.Contains(p.BookingId) && p.Status == "Succeeded")
                    .Select(p => new { p.BookingId, p.Amount })
                    .ToListAsync())
                .GroupBy(p => p.BookingId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            var vendorIds = bookings.Select(b => b.VendorId).Distinct().ToList();
            var vendors = await _db.Vendors.AsNoTracking()
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.BusinessName);
            var packageIds = bookings.Where(b => b.PackageId.HasValue).Select(b => b.PackageId!.Value).Distinct().ToList();
            var packages = await _db.Packages.AsNoTracking()
                .Where(p => packageIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            return bookings.Select(b =>
            {
                paid.TryGetValue(b.Id, out var amountPaid);
                return new BookingCard
                {
                    Id = b.Id.ToString(),
                    BookingNumber = $"BK-{b.Id.ToString()[..8].ToUpper()}",
                    EventName = string.IsNullOrWhiteSpace(b.EventName) ? "Event" : b.EventName,
                    EventDate = b.EventDate.ToString("yyyy-MM-dd"),
                    Status = BookingStatuses.ToClient(b.Status),
                    VendorName = vendors.TryGetValue(b.VendorId, out var v) ? v : "",
                    PackageName = b.PackageId.HasValue && packages.TryGetValue(b.PackageId.Value, out var n) ? n : "",
                    Guests = b.GuestCount,
                    TotalAmount = b.TotalAmount,
                    AmountPaid = amountPaid,
                    BalanceDue = Math.Max(0, b.TotalAmount - amountPaid),
                    AdvanceAmount = b.AdvanceAmount,
                    EventDay = b.EventDate.Date
                };
            }).ToList();
        }

        /// <summary>
        /// What the customer would get back by cancelling today: the same schedule the cancellation
        /// endpoint applies to a customer cancellation (BookingController.CancelBooking).
        /// </summary>
        public static (decimal Refund, string Rule) RefundIfCancelledToday(BookingCard booking, DateTime today)
        {
            if (booking.Status == "pending") return (0m, "it is not paid yet, so cancelling is free");
            int days = (booking.EventDay - today.Date).Days;
            decimal advance = booking.AdvanceAmount;
            if (days > 30)
            {
                var fee = Math.Min(Math.Round(booking.TotalAmount * 0.02m), 2500m);
                return (Math.Max(0m, advance - fee), $"it is {days} days away: the advance minus a platform fee of Rs {fee:N0}");
            }
            if (days >= 15) return (advance * 0.5m, $"it is {days} days away: 50% of the advance");
            if (days >= 7) return (advance * 0.25m, $"it is {days} days away: 25% of the advance");
            return (0m, $"it is {Math.Max(days, 0)} days away: no refund within 7 days of the event");
        }

        public async Task<RewardsCard> MyRewardsAsync(Guid userId)
        {
            var balance = await _loyalty.GetBalanceAsync(userId);
            return new RewardsCard
            {
                Points = balance.Points,
                Tier = balance.Tier,
                NextTier = balance.Tier switch { "Bronze" => "Silver", "Silver" => "Gold", _ => null },
                PointsToNextTier = balance.PointsToNextTier
            };
        }

        /// <summary>
        /// How JoinEvents works, taken from the rules the code enforces (advance rate from config,
        /// refund schedule from the cancellation endpoint, loyalty from the loyalty service), so the
        /// assistant never quotes a policy the platform does not apply.
        /// </summary>
        public string PlatformFacts() => $"""
            BOOKING FLOW
            - Customers browse packages by category, open a package to see what it includes, and book it for a date and guest count. The price is quoted by the server for that guest count and includes 18% GST.
            - At checkout the customer pays either the {AdvancePercent}% advance or the full amount, by UPI, card, net banking or wallet.
            - A booking stays "pending" until it is paid. Once paid ("advance paid") the vendor confirms it; on confirmation a chat with the vendor opens in Messages automatically.
            - Statuses: pending (not paid yet), advance_paid (paid, waiting for the vendor to confirm), confirmed, in_progress (the event is happening), completed, settled (all payments closed), cancelled, rejected, disputed.
            - The remaining balance is paid from the booking's page before or after the event.

            CANCELLATION AND REFUNDS (customer cancels)
            - A pending (unpaid) booking can be cancelled free of charge.
            - Paid booking, more than 30 days before the event: the advance is refunded minus a platform fee of 2% of the booking total (at most Rs 2,500).
            - 15 to 30 days before: 50% of the advance is refunded.
            - 7 to 14 days before: 25% of the advance is refunded.
            - Less than 7 days before: no refund.
            - If the vendor cancels, the customer gets the full advance back and the vendor pays a penalty.
            - Cancel from the booking's page; the refund is shown before confirming.

            LOYALTY REWARDS
            - 10 points for every Rs 100 paid, and 50 points for reviewing a completed booking.
            - Tiers: Bronze (0 to 4,999 points), Silver (5,000+), Gold (15,000+).

            QUOTES
            - Customers can post a quote request describing their event (type, date, guests, budget, city) and compare quotes from vendors, then accept one.

            HELP
            - Support tickets are raised from the Support screen; messages with a vendor are in Messages.
            """;
    }
}
