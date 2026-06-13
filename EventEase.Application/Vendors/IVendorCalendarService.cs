using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static EventEase.Application.Vendors.Dtos;

namespace EventEase.Application.Vendors
{
    public interface IVendorCalendarService
    {
        Task<List<CalendarDayDto>> GetCalendarAsync(Guid vendorId, int? month, int? year);
        Task<CalendarDayDto> ToggleBlockedDateAsync(Guid vendorId, DateTime date, string? reason);
        Task<bool> CheckAvailabilityAsync(Guid vendorId, DateTime date);
        Task<Dictionary<Guid, bool>> CheckBulkAvailabilityAsync(IEnumerable<Guid> vendorIds, DateTime date);
        Task<List<CalendarDayDto>> BlockDatesAsync(Guid vendorId, IEnumerable<DateTime> dates, string? reason);
        Task<List<CalendarDayDto>> ReleaseDatesAsync(Guid vendorId, IEnumerable<DateTime> dates);
    }
}
