using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static EventEase.Application.Vendors.Dtos;

namespace EventEase.Application.Vendors
{
    public class VendorCalendarService : IVendorCalendarService
    {
        private readonly EventEaseDbContext _db;

        public VendorCalendarService(EventEaseDbContext db)
        {
            _db = db;
        }

        public async Task<List<CalendarDayDto>> GetCalendarAsync(Guid vendorId, int? month, int? year)
        {
            int targetMonth = month ?? DateTime.UtcNow.Month;
            int targetYear = year ?? DateTime.UtcNow.Year;

            int daysInMonth = DateTime.DaysInMonth(targetYear, targetMonth);
            var startDate = new DateTime(targetYear, targetMonth, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // Fetch active bookings in range (excluding Cancelled/Rejected)
            var bookings = await _db.Bookings
                .Where(b => b.VendorId == vendorId &&
                            b.EventDate >= startDate &&
                            b.EventDate <= endDate &&
                            b.Status != "Cancelled" &&
                            b.Status != "Rejected")
                .AsNoTracking()
                .ToListAsync();

            // Fetch blocked dates in range
            var blockedDates = await _db.VendorBlockedDates
                .Where(d => d.VendorId == vendorId &&
                            d.BlockedDate >= startDate &&
                            d.BlockedDate <= endDate)
                .AsNoTracking()
                .ToListAsync();

            var result = new List<CalendarDayDto>();

            for (int d = 1; d <= daysInMonth; d++)
            {
                var currentDate = new DateTime(targetYear, targetMonth, d, 0, 0, 0, DateTimeKind.Utc);
                string dateStr = currentDate.ToString("yyyy-MM-dd");

                var booking = bookings.FirstOrDefault(b => b.EventDate.Date == currentDate.Date);
                if (booking != null)
                {
                    result.Add(new CalendarDayDto(dateStr, "booked", booking.Id.ToString()));
                    continue;
                }

                var isBlocked = blockedDates.Any(bd => bd.BlockedDate.Date == currentDate.Date);
                if (isBlocked)
                {
                    result.Add(new CalendarDayDto(dateStr, "blocked"));
                    continue;
                }

                result.Add(new CalendarDayDto(dateStr, "available"));
            }

            return result;
        }

        public async Task<CalendarDayDto> ToggleBlockedDateAsync(Guid vendorId, DateTime date, string? reason)
        {
            var targetDate = date.Date;

            // Check if there is an active booking on this date
            var hasBooking = await _db.Bookings.AnyAsync(b => b.VendorId == vendorId && 
                                                             b.EventDate.Date == targetDate && 
                                                             b.Status != "Cancelled" && 
                                                             b.Status != "Rejected");
            if (hasBooking)
            {
                throw new InvalidOperationException("Cannot block or unblock a date that already has an active booking.");
            }

            var existingBlock = await _db.VendorBlockedDates
                .FirstOrDefaultAsync(d => d.VendorId == vendorId && d.BlockedDate.Date == targetDate);

            string newStatus;

            if (existingBlock != null)
            {
                _db.VendorBlockedDates.Remove(existingBlock);
                newStatus = "available";
            }
            else
            {
                var block = new VendorBlockedDate
                {
                    Id = Guid.NewGuid(),
                    VendorId = vendorId,
                    BlockedDate = targetDate,
                    Reason = reason,
                    CreatedAt = DateTime.UtcNow
                };
                _db.VendorBlockedDates.Add(block);
                newStatus = "blocked";
            }

            await _db.SaveChangesAsync();
            return new CalendarDayDto(targetDate.ToString("yyyy-MM-dd"), newStatus);
        }

        public async Task<bool> CheckAvailabilityAsync(Guid vendorId, DateTime date)
        {
            var targetDate = date.Date;

            var hasBooking = await _db.Bookings.AnyAsync(b => b.VendorId == vendorId && 
                                                             b.EventDate.Date == targetDate && 
                                                             b.Status != "Cancelled" && 
                                                             b.Status != "Rejected");
            if (hasBooking) return false;

            var isBlocked = await _db.VendorBlockedDates.AnyAsync(d => d.VendorId == vendorId && 
                                                                     d.BlockedDate.Date == targetDate);
            return !isBlocked;
        }

        public async Task<Dictionary<Guid, bool>> CheckBulkAvailabilityAsync(IEnumerable<Guid> vendorIds, DateTime date)
        {
            var targetDate = date.Date;
            var uniqueVendorIds = vendorIds.Distinct().ToList();

            if (!uniqueVendorIds.Any())
            {
                return new Dictionary<Guid, bool>();
            }

            var bookedVendors = await _db.Bookings
                .Where(b => uniqueVendorIds.Contains(b.VendorId) && 
                            b.EventDate.Date == targetDate && 
                            b.Status != "Cancelled" && 
                            b.Status != "Rejected")
                .Select(b => b.VendorId)
                .Distinct()
                .ToListAsync();

            var blockedVendors = await _db.VendorBlockedDates
                .Where(d => uniqueVendorIds.Contains(d.VendorId) && 
                            d.BlockedDate.Date == targetDate)
                .Select(d => d.VendorId)
                .Distinct()
                .ToListAsync();

            var result = new Dictionary<Guid, bool>();

            foreach (var vendorId in uniqueVendorIds)
            {
                bool isAvailable = !bookedVendors.Contains(vendorId) && !blockedVendors.Contains(vendorId);
                result[vendorId] = isAvailable;
            }

            return result;
        }
    }
}
