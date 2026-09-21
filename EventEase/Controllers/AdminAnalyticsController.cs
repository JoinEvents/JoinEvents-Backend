using EventEase.Core.Constants;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Api.Controllers
{
    /// <summary>
    /// The figures on the admin home screen.
    /// </summary>
    /// <remarks>
    /// This route did not exist. The app has always called it, so the overview was always
    /// null, and the dashboard template has no branch for a null overview — the whole top of
    /// the screen rendered as nothing, which is what an administrator saw after signing in.
    /// </remarks>
    [ApiController]
    [Route("api/v1/admin/analytics")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public class AdminAnalyticsController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public AdminAnalyticsController(EventEaseDbContext db) => _db = db;

        /// <summary>Statuses where money has actually changed hands.</summary>
        private static readonly string[] Realised =
        {
            BookingStatuses.Paid, BookingStatuses.Confirmed,
            BookingStatuses.InProgress, BookingStatuses.Completed, BookingStatuses.Settled
        };

        [HttpGet]
        public async Task<IActionResult> GetOverview()
        {
            var bookings = await _db.Bookings
                .Select(b => new { b.Status, b.TotalAmount, b.PlatformFeeAmount })
                .ToListAsync();

            // Gross value counts bookings that were actually paid for, so a request that was
            // only ever made does not read as money through the platform.
            var realised = bookings
                .Where(b => Realised.Contains(b.Status, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var overview = new
            {
                totalCustomers = await _db.Users.CountAsync(u => u.Role == AuthRoles.Customer),
                totalVendors = await _db.Vendors.CountAsync(),
                pendingVerifications = await _db.Vendors.CountAsync(v => !v.IsValidated),
                totalBookings = bookings.Count,
                grossBookingValue = realised.Sum(b => b.TotalAmount),
                platformRevenue = realised.Sum(b => b.PlatformFeeAmount),
                openDisputes = bookings.Count(b =>
                    string.Equals(b.Status, BookingStatuses.Disputed, StringComparison.OrdinalIgnoreCase)),
                activePackages = await _db.Packages.CountAsync(p => p.IsActive)
            };

            return Ok(overview);
        }
    }
}
