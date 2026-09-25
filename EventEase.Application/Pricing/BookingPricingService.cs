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

    /// <summary>
    /// The authoritative, server-computed cost of a booking. <see cref="Lines"/> are before GST and
    /// add up to <see cref="Subtotal"/>; <see cref="TotalAmount"/> is the subtotal plus GST.
    /// </summary>
    public record BookingPriceResult(
        decimal TotalAmount,
        decimal AdvanceAmount,
        decimal PlatformFeeRate,
        decimal PlatformFeeAmount,
        decimal VendorPayoutAmount,
        string? PackageName,
        IReadOnlyList<BookingPriceLine> Lines)
    {
        public decimal Subtotal { get; init; }
        public decimal GstRate { get; init; }
        public decimal GstAmount { get; init; }
        public decimal AdvanceRate { get; init; }
        public int? MaxGuests { get; init; }
    }

    /// <summary>One priced item. <see cref="Detail"/> explains the amount, e.g. "₹450 × 200 guests".</summary>
    public record BookingPriceLine(string Description, decimal Amount)
    {
        public string? Detail { get; init; }
    }

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
            decimal subtotal = 0m;
            decimal gst = 0m;
            string? packageName = null;
            int? maxGuests = null;

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
                maxGuests = package.Capacity?.MaxGuests is > 0 ? package.Capacity.MaxGuests : null;
                if (maxGuests.HasValue && request.GuestCount > maxGuests.Value)
                    throw new BusinessRuleException($"This package caters for up to {maxGuests.Value} guests.");

                var services = PackageInclusionPricing.Parse(package.Description);
                if (services is not null)
                {
                    // Priced per service for the guests actually booked: per-plate catering scales
                    // with the guest count, everything else is a flat price. GST is added on top.
                    decimal packageSubtotal = 0m;
                    foreach (var service in services)
                    {
                        var amount = Round(service.AmountFor(request.GuestCount));
                        if (amount <= 0) continue;
                        packageSubtotal += amount;
                        lines.Add(new BookingPriceLine(service.Name, amount)
                        {
                            Detail = service.PerPlate ? $"₹{service.UnitPrice:0.##} per plate × {request.GuestCount} guests" : null
                        });
                    }
                    if (packageSubtotal <= 0)
                        throw new BusinessRuleException("The selected package has no price configured. Please contact the vendor.");

                    subtotal += packageSubtotal;
                    gst += Round(packageSubtotal * PackageInclusionPricing.GstRate);
                }
                else
                {
                    var packageAmount = PricePackage(package.Pricing, request.GuestCount, request.MealPreference);
                    if (packageAmount <= 0)
                        throw new BusinessRuleException("The selected package has no price configured. Please contact the vendor.");

                    AddGstInclusive(package.Name, packageAmount);
                }
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
                    AddGstInclusive(service.Name, service.Price);
                }
            }

            var total = subtotal + gst;
            if (total <= 0)
                throw new BusinessRuleException("A booking must include at least one package or service.");

            var platformFeeAmount = Round(total * _platformFeeRate);
            var advanceAmount = Round(total * _advanceRate);
            var vendorPayoutAmount = total - platformFeeAmount;

            return new BookingPriceResult(
                TotalAmount: total,
                AdvanceAmount: advanceAmount,
                PlatformFeeRate: _platformFeeRate,
                PlatformFeeAmount: platformFeeAmount,
                VendorPayoutAmount: vendorPayoutAmount,
                PackageName: packageName,
                Lines: lines)
            {
                Subtotal = subtotal,
                GstRate = PackageInclusionPricing.GstRate,
                GstAmount = gst,
                AdvanceRate = _advanceRate,
                MaxGuests = maxGuests
            };

            // Catalogue prices other than per-service package details already include GST, so the
            // GST share is split out of the amount rather than added to it.
            void AddGstInclusive(string description, decimal amount)
            {
                var gross = Round(amount);
                var net = Round(gross / (1 + PackageInclusionPricing.GstRate));
                subtotal += net;
                gst += gross - net;
                lines.Add(new BookingPriceLine(description, net));
            }
        }

        private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

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
