using EventEase.Core.Exceptions;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EventEase.Application.Pricing
{
    /// <summary>What the caller is asking to book. Contains no money: prices are never accepted from clients.</summary>
    public record BookingPriceRequest(
        Guid VendorId,
        Guid? PackageId,
        int GuestCount,
        IReadOnlyList<Guid>? ServiceIds,
        string? MealPreference);

    /// <summary>The authoritative, server-computed cost of a booking.</summary>
    public record BookingPriceResult(
        decimal TotalAmount,
        decimal AdvanceAmount,
        decimal PlatformFeeRate,
        decimal PlatformFeeAmount,
        decimal VendorPayoutAmount,
        string? PackageName,
        IReadOnlyList<BookingPriceLine> Lines);

    public record BookingPriceLine(string Description, decimal Amount);

    public interface IBookingPricingService
    {
        Task<BookingPriceResult> PriceAsync(BookingPriceRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Computes booking totals from catalogue data owned by the vendor being booked.
    ///
    /// This exists because the amount a booking is worth must never come from the request body —
    /// otherwise a caller can set their own price.
    /// </summary>
    public class BookingPricingService : IBookingPricingService
    {
        private readonly EventEaseDbContext _db;
        private readonly decimal _platformFeeRate;
        private readonly decimal _advanceRate;

        public BookingPricingService(EventEaseDbContext db, IConfiguration config)
        {
            _db = db;
            _platformFeeRate = config.GetValue("Pricing:PlatformFeeRate", 0.10m);
            _advanceRate = config.GetValue("Pricing:AdvanceRate", 0.30m);
        }

        public async Task<BookingPriceResult> PriceAsync(BookingPriceRequest request, CancellationToken cancellationToken = default)
        {
            if (request.GuestCount <= 0)
                throw new BusinessRuleException("Guest count must be greater than zero.");
            if (request.GuestCount > 100_000)
                throw new BusinessRuleException("Guest count exceeds the supported maximum.");

            var lines = new List<BookingPriceLine>();
            decimal total = 0m;
            string? packageName = null;

            if (request.PackageId.HasValue)
            {
                var package = await _db.Packages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == request.PackageId.Value, cancellationToken);

                if (package is null)
                    throw new BusinessRuleException("The selected package does not exist.");
                if (package.VendorId != request.VendorId)
                    throw new BusinessRuleException("The selected package does not belong to this vendor.");
                if (!package.IsActive)
                    throw new BusinessRuleException("The selected package is no longer available.");

                packageName = package.Name;

                var packageAmount = PricePackage(package.Pricing, request.GuestCount, request.MealPreference);
                if (packageAmount <= 0)
                    throw new BusinessRuleException("The selected package has no price configured. Please contact the vendor.");

                total += packageAmount;
                lines.Add(new BookingPriceLine(package.Name, packageAmount));
            }

            if (request.ServiceIds is { Count: > 0 })
            {
                var requestedIds = request.ServiceIds.Distinct().ToList();

                // Only services belonging to the vendor being booked may be added, so a caller
                // cannot attach a cheap service from an unrelated vendor.
                var services = await _db.Services
                    .AsNoTracking()
                    .Where(s => s.VendorId == request.VendorId && requestedIds.Contains(s.Id))
                    .ToListAsync(cancellationToken);

                if (services.Count != requestedIds.Count)
                    throw new BusinessRuleException("One or more selected services are not offered by this vendor.");

                foreach (var service in services)
                {
                    total += service.Price;
                    lines.Add(new BookingPriceLine(service.Name, service.Price));
                }
            }

            if (total <= 0)
                throw new BusinessRuleException("A booking must include at least one package or service.");

            total = decimal.Round(total, 2, MidpointRounding.AwayFromZero);

            var platformFeeAmount = decimal.Round(total * _platformFeeRate, 2, MidpointRounding.AwayFromZero);
            var advanceAmount = decimal.Round(total * _advanceRate, 2, MidpointRounding.AwayFromZero);
            var vendorPayoutAmount = total - platformFeeAmount;

            return new BookingPriceResult(
                TotalAmount: total,
                AdvanceAmount: advanceAmount,
                PlatformFeeRate: _platformFeeRate,
                PlatformFeeAmount: platformFeeAmount,
                VendorPayoutAmount: vendorPayoutAmount,
                PackageName: packageName,
                Lines: lines);
        }

        /// <summary>
        /// Resolves a package's price. A flat BasePrice or Rent wins; otherwise the per-plate rate
        /// is multiplied by the guest count.
        /// </summary>
        private static decimal PricePackage(Core.Entities.PackagePricing pricing, int guestCount, string? mealPreference)
        {
            if (pricing.BasePrice is > 0) return pricing.BasePrice.Value;
            if (pricing.Rent is > 0) return pricing.Rent.Value;

            var perPlate = mealPreference?.Trim().ToLowerInvariant() switch
            {
                "nonveg" or "non-veg" or "nonvegetarian" => pricing.NonVegPrice ?? pricing.VegPrice,
                _ => pricing.VegPrice ?? pricing.NonVegPrice
            };

            if (perPlate is > 0) return perPlate.Value * guestCount;
            if (pricing.RoomPrice is > 0) return pricing.RoomPrice.Value;

            return 0m;
        }
    }
}
