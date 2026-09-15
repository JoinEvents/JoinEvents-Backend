using EventEase.Application.Pricing;
using EventEase.Core.Entities;
using EventEase.Core.Exceptions;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// Booking amounts used to be taken from the request body, so a caller could set their own
    /// price. These tests pin the server-side pricing rules that replaced that.
    /// </summary>
    public class BookingPricingServiceTests : IDisposable
    {
        private readonly EventEaseDbContext _db;
        private readonly BookingPricingService _pricing;

        private readonly Guid _vendorId = Guid.NewGuid();
        private readonly Guid _otherVendorId = Guid.NewGuid();

        public BookingPricingServiceTests()
        {
            var options = new DbContextOptionsBuilder<EventEaseDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            _db = new EventEaseDbContext(options);
            _db.Database.EnsureCreated();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Pricing:PlatformFeeRate"] = "0.10",
                    ["Pricing:AdvanceRate"] = "0.30"
                })
                .Build();

            _pricing = new BookingPricingService(_db, config);
        }

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
        }

        private Package AddPackage(Guid vendorId, decimal basePrice, bool isActive = true)
        {
            var package = new Package
            {
                Id = Guid.NewGuid(),
                VendorId = vendorId,
                Name = "Test Package",
                Category = "Wedding",
                IsActive = isActive,
                Pricing = new PackagePricing { BasePrice = basePrice }
            };
            _db.Packages.Add(package);
            _db.SaveChanges();
            return package;
        }

        [Fact]
        public async Task UsesCataloguePrice_NotAnythingSuppliedByTheCaller()
        {
            var package = AddPackage(_vendorId, 100_000m);

            var result = await _pricing.PriceAsync(
                new BookingPriceRequest(_vendorId, package.Id, 100, null, null));

            Assert.Equal(100_000m, result.TotalAmount);
            Assert.Equal(10_000m, result.PlatformFeeAmount);
            Assert.Equal(30_000m, result.AdvanceAmount);
            Assert.Equal(90_000m, result.VendorPayoutAmount);
        }

        [Fact]
        public async Task RejectsPackageBelongingToADifferentVendor()
        {
            var package = AddPackage(_otherVendorId, 100_000m);

            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                _pricing.PriceAsync(new BookingPriceRequest(_vendorId, package.Id, 100, null, null)));
        }

        [Fact]
        public async Task RejectsInactivePackage()
        {
            var package = AddPackage(_vendorId, 50_000m, isActive: false);

            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                _pricing.PriceAsync(new BookingPriceRequest(_vendorId, package.Id, 100, null, null)));
        }

        [Fact]
        public async Task RejectsServicesOfferedByADifferentVendor()
        {
            var package = AddPackage(_vendorId, 50_000m);
            var foreignService = new Service
            {
                Id = Guid.NewGuid(),
                VendorId = _otherVendorId,
                Name = "Cheap Catering",
                Category = "Food",
                Price = 1m
            };
            _db.Services.Add(foreignService);
            _db.SaveChanges();

            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                _pricing.PriceAsync(new BookingPriceRequest(
                    _vendorId, package.Id, 100, new[] { foreignService.Id }, null)));
        }

        [Fact]
        public async Task AddsTheVendorsOwnServicesToTheTotal()
        {
            var package = AddPackage(_vendorId, 50_000m);
            var service = new Service
            {
                Id = Guid.NewGuid(),
                VendorId = _vendorId,
                Name = "Photography",
                Category = "Media",
                Price = 30_000m
            };
            _db.Services.Add(service);
            _db.SaveChanges();

            var result = await _pricing.PriceAsync(new BookingPriceRequest(
                _vendorId, package.Id, 100, new[] { service.Id }, null));

            Assert.Equal(80_000m, result.TotalAmount);
            Assert.Equal(2, result.Lines.Count);
        }

        [Fact]
        public async Task PerPlatePricingMultipliesByGuestCount()
        {
            var package = new Package
            {
                Id = Guid.NewGuid(),
                VendorId = _vendorId,
                Name = "Per Plate",
                Category = "Wedding",
                IsActive = true,
                Pricing = new PackagePricing { VegPrice = 500m, NonVegPrice = 800m }
            };
            _db.Packages.Add(package);
            _db.SaveChanges();

            var veg = await _pricing.PriceAsync(new BookingPriceRequest(_vendorId, package.Id, 200, null, "veg"));
            Assert.Equal(100_000m, veg.TotalAmount);

            var nonVeg = await _pricing.PriceAsync(new BookingPriceRequest(_vendorId, package.Id, 200, null, "nonveg"));
            Assert.Equal(160_000m, nonVeg.TotalAmount);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task RejectsNonPositiveGuestCount(int guestCount)
        {
            var package = AddPackage(_vendorId, 100_000m);

            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                _pricing.PriceAsync(new BookingPriceRequest(_vendorId, package.Id, guestCount, null, null)));
        }

        [Fact]
        public async Task RejectsABookingWithNoPackageAndNoServices()
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                _pricing.PriceAsync(new BookingPriceRequest(_vendorId, null, 100, null, null)));
        }
    }
}
