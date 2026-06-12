using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Application.Vendors;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EventEase.Tests
{
    public class VendorCalendarServiceTests : IDisposable
    {
        private readonly EventEaseDbContext _db;
        private readonly VendorCalendarService _service;
        private readonly Guid _vendorId = Guid.NewGuid();

        public VendorCalendarServiceTests()
        {
            var options = new DbContextOptionsBuilder<EventEaseDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _db = new EventEaseDbContext(options);
            _db.Database.EnsureCreated();
            _service = new VendorCalendarService(_db);
        }

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
        }

        [Fact]
        public async Task GetCalendarAsync_ShouldReturnCorrectDaysAndStatus()
        {
            // Arrange
            var bookingDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var blockedDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc);
            var cancelledBookingDate = new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc);

            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                VendorId = _vendorId,
                EventDate = bookingDate,
                Status = "Confirmed",
                EventName = "Wedding",
                City = "Mumbai",
                Venue = "Grand Hall",
                UserId = Guid.NewGuid()
            };

            var cancelledBooking = new Booking
            {
                Id = Guid.NewGuid(),
                VendorId = _vendorId,
                EventDate = cancelledBookingDate,
                Status = "Cancelled",
                EventName = "Birthday",
                City = "Mumbai",
                Venue = "Grand Hall",
                UserId = Guid.NewGuid()
            };

            var block = new VendorBlockedDate
            {
                Id = Guid.NewGuid(),
                VendorId = _vendorId,
                BlockedDate = blockedDate,
                Reason = "Personal Holiday",
                CreatedAt = DateTime.UtcNow
            };

            _db.Bookings.AddRange(booking, cancelledBooking);
            _db.VendorBlockedDates.Add(block);
            await _db.SaveChangesAsync();

            // Act
            var calendar = await _service.GetCalendarAsync(_vendorId, 6, 2026);

            // Assert
            Assert.Equal(30, calendar.Count); // June has 30 days
            
            var bookedDay = calendar.First(d => d.Date == "2026-06-15");
            Assert.Equal("booked", bookedDay.Status);
            Assert.Equal(booking.Id.ToString(), bookedDay.BookingId);

            var blockedDay = calendar.First(d => d.Date == "2026-06-20");
            Assert.Equal("blocked", blockedDay.Status);

            var cancelledDay = calendar.First(d => d.Date == "2026-06-25");
            Assert.Equal("available", cancelledDay.Status);

            var normalDay = calendar.First(d => d.Date == "2026-06-01");
            Assert.Equal("available", normalDay.Status);
        }

        [Fact]
        public async Task ToggleBlockedDateAsync_ShouldAddBlock_WhenNotBlocked()
        {
            // Arrange
            var dateToBlock = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

            // Act
            var result = await _service.ToggleBlockedDateAsync(_vendorId, dateToBlock, "Holiday");

            // Assert
            Assert.Equal("blocked", result.Status);
            Assert.Equal("2026-06-10", result.Date);

            var existsInDb = await _db.VendorBlockedDates.AnyAsync(b => b.VendorId == _vendorId && b.BlockedDate.Date == dateToBlock.Date);
            Assert.True(existsInDb);
        }

        [Fact]
        public async Task ToggleBlockedDateAsync_ShouldRemoveBlock_WhenAlreadyBlocked()
        {
            // Arrange
            var dateToUnblock = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
            var block = new VendorBlockedDate
            {
                Id = Guid.NewGuid(),
                VendorId = _vendorId,
                BlockedDate = dateToUnblock,
                CreatedAt = DateTime.UtcNow
            };
            _db.VendorBlockedDates.Add(block);
            await _db.SaveChangesAsync();

            // Act
            var result = await _service.ToggleBlockedDateAsync(_vendorId, dateToUnblock, null);

            // Assert
            Assert.Equal("available", result.Status);
            Assert.Equal("2026-06-10", result.Date);

            var existsInDb = await _db.VendorBlockedDates.AnyAsync(b => b.VendorId == _vendorId && b.BlockedDate.Date == dateToUnblock.Date);
            Assert.False(existsInDb);
        }

        [Fact]
        public async Task ToggleBlockedDateAsync_ShouldThrowException_WhenBookingExists()
        {
            // Arrange
            var dateWithBooking = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                VendorId = _vendorId,
                EventDate = dateWithBooking,
                Status = "Pending",
                EventName = "Corporate",
                City = "Mumbai",
                Venue = "Grand Hall",
                UserId = Guid.NewGuid()
            };
            _db.Bookings.Add(booking);
            await _db.SaveChangesAsync();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ToggleBlockedDateAsync(_vendorId, dateWithBooking, "Trying to block"));
        }

        [Fact]
        public async Task CheckAvailabilityAsync_ShouldReturnCorrectAvailability()
        {
            // Arrange
            var bookingDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var blockedDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc);
            var availableDate = new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc);

            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                VendorId = _vendorId,
                EventDate = bookingDate,
                Status = "Paid",
                EventName = "Corporate",
                City = "Mumbai",
                Venue = "Grand Hall",
                UserId = Guid.NewGuid()
            };

            var block = new VendorBlockedDate
            {
                Id = Guid.NewGuid(),
                VendorId = _vendorId,
                BlockedDate = blockedDate,
                CreatedAt = DateTime.UtcNow
            };

            _db.Bookings.Add(booking);
            _db.VendorBlockedDates.Add(block);
            await _db.SaveChangesAsync();

            // Act & Assert
            Assert.False(await _service.CheckAvailabilityAsync(_vendorId, bookingDate));
            Assert.False(await _service.CheckAvailabilityAsync(_vendorId, blockedDate));
            Assert.True(await _service.CheckAvailabilityAsync(_vendorId, availableDate));
        }

        [Fact]
        public async Task CheckBulkAvailabilityAsync_ShouldReturnAvailabilityMap()
        {
            // Arrange
            var date = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var vendor1 = Guid.NewGuid();
            var vendor2 = Guid.NewGuid();
            var vendor3 = Guid.NewGuid();

            // Vendor 1 is booked
            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                VendorId = vendor1,
                EventDate = date,
                Status = "Confirmed",
                EventName = "Wedding",
                City = "Mumbai",
                Venue = "Grand Hall",
                UserId = Guid.NewGuid()
            };

            // Vendor 2 is blocked
            var block = new VendorBlockedDate
            {
                Id = Guid.NewGuid(),
                VendorId = vendor2,
                BlockedDate = date,
                CreatedAt = DateTime.UtcNow
            };

            // Vendor 3 is free

            _db.Bookings.Add(booking);
            _db.VendorBlockedDates.Add(block);
            await _db.SaveChangesAsync();

            // Act
            var result = await _service.CheckBulkAvailabilityAsync(new[] { vendor1, vendor2, vendor3 }, date);

            // Assert
            Assert.Equal(3, result.Count);
            Assert.False(result[vendor1]);
            Assert.False(result[vendor2]);
            Assert.True(result[vendor3]);
        }
    }
}
