using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Vendors
{
    public class Dtos
    {
        public record VendorRegisterDto(string BusinessName, string Description, string Location);
        public record VendorDto(Guid Id, string BusinessName, string Location, bool IsValidated);
        public record ModerateVendorDto(string Action, string Reason, string Duration);
        public record ReviewDocumentDto(string Status, string RejectionReason);
        public record VendorAnalyticsResponse(decimal TotalEarnings, int ActiveBookings, int PendingBidsCount, object[] MonthlyRevenue, double AverageRating);
        public record CalendarDayDto(string Date, string Status, string? BookingId = null);
        public record ToggleBlockedDateRequest(string Date, string? Reason = null);
    }
}
