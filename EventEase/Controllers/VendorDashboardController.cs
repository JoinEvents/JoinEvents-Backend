using EventEase.Core.Constants;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/vendor/dashboard")]
    public class VendorDashboardController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public VendorDashboardController(EventEaseDbContext db)
        {
            _db = db;
        }

        /// <summary>Statuses where the vendor has earned the money, not merely been asked.</summary>
        private static readonly string[] Earned =
        {
            BookingStatuses.Paid, BookingStatuses.Confirmed,
            BookingStatuses.Completed, BookingStatuses.Settled
        };

        private static readonly string[] Cancelled =
        {
            BookingStatuses.Cancelled, BookingStatuses.Rejected
        };

        /// <summary>
        /// A percentage the vendor can act on: each step is something the app links to,
        /// so "80% complete" always has a visible next step behind it.
        /// </summary>
        private static int ProfileCompletion(Core.Entities.Vendor vendor, int activePackages)
        {
            var steps = new[]
            {
                !string.IsNullOrWhiteSpace(vendor.BusinessName),
                !string.IsNullOrWhiteSpace(vendor.Description),
                !string.IsNullOrWhiteSpace(vendor.Location),
                vendor.IsValidated,
                activePackages > 0
            };
            return (int)Math.Round(steps.Count(done => done) * 100.0 / steps.Length);
        }

        private Guid GetUserId()
        {
            var val = User.FindFirstValue(ClaimTypes.NameIdentifier) 
                      ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? User.FindFirstValue("sub")
                      ?? User.FindFirstValue("id");
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }

        [Authorize(Policy = "Vendor")]
        [HttpGet]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            
            if (vendor == null)
            {
                // Create a basic vendor profile if it doesn't exist
                vendor = new Core.Entities.Vendor { 
                    Id = Guid.NewGuid(), 
                    UserId = userId, 
                    BusinessName = "My Vendor Business", 
                    IsValidated = false,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Vendors.Add(vendor);
                await _db.SaveChangesAsync();
            }

            var bookings = await _db.Bookings
                .Where(b => b.VendorId == vendor.Id)
                .ToListAsync();

            // Revenue counts money the vendor has actually earned, so a booking that
            // was only ever requested does not inflate it.
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthlyRevenue = bookings
                .Where(b => Earned.Contains(b.Status, StringComparer.OrdinalIgnoreCase))
                .Where(b => b.EventDate >= monthStart)
                .Sum(b => b.TotalAmount);

            var pendingRequests = bookings
                .Count(b => string.Equals(b.Status, BookingStatuses.Pending, StringComparison.OrdinalIgnoreCase));

            var reviews = await _db.Reviews
                .Where(r => r.VendorId == vendor.Id && r.Status != "removed")
                .ToListAsync();

            var activePackages = await _db.Packages
                .CountAsync(p => p.VendorId == vendor.Id && p.IsActive);

            // The next few confirmed jobs, which is what the vendor is looking for.
            var upcoming = bookings
                .Where(b => b.EventDate >= DateTime.UtcNow.Date)
                .Where(b => !Cancelled.Contains(b.Status, StringComparer.OrdinalIgnoreCase))
                .OrderBy(b => b.EventDate)
                .Take(5)
                .ToList();

            var customerIds = upcoming.Select(b => b.UserId).Distinct().ToList();
            var customerNames = await _db.Users
                .Where(u => customerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);

            var dashboard = new
            {
                totalBookings = bookings.Count,
                pendingRequests,
                monthlyRevenue,
                rating = reviews.Count > 0 ? Math.Round(reviews.Average(r => r.Rating), 1) : 0d,
                totalReviews = reviews.Count,
                activePackages,
                profileCompletion = ProfileCompletion(vendor, activePackages),
                verificationStatus = vendor.IsValidated ? "verified" : "pending",
                upcomingEvents = upcoming.Select(b => new
                {
                    id = b.Id,
                    name = b.EventName,
                    date = b.EventDate,
                    customerName = customerNames.TryGetValue(b.UserId, out var name) ? name : "Customer"
                })
            };
            return Ok(dashboard);
        }

        [Authorize(Policy = "Vendor")]
        [HttpGet("tasks")]
        public async Task<IActionResult> GetTasks()
        {
            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            
            if (vendor == null)
            {
                vendor = new Core.Entities.Vendor { 
                    Id = Guid.NewGuid(), 
                    UserId = userId, 
                    BusinessName = "My Vendor Business", 
                    IsValidated = false,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Vendors.Add(vendor);
                await _db.SaveChangesAsync();
            }

            var tasks = new List<object>();

            if (string.IsNullOrWhiteSpace(vendor.Description))
            {
                tasks.Add(new { id = "t1", title = "Business Introduction", link = "/vendor/profile" });
            }

            if (!vendor.IsValidated)
            {
                tasks.Add(new { id = "t2", title = "Profile KYC", link = "/vendor/verification" });
            }
            else
            {
                var packages = await _db.Packages.Where(p => p.VendorId == vendor.Id || p.VendorId == vendor.UserId).ToListAsync();
                if (!packages.Any())
                {
                    tasks.Add(new { id = "t3", title = "Create Service Package", link = "/vendor/add-service" });
                }
                else
                {
                    var packageIds = packages.Select(p => p.Id).ToList();
                    var images = await _db.PackageImages.Where(i => packageIds.Contains(i.PackageId)).ToListAsync();
                    if (!images.Any())
                    {
                        tasks.Add(new { id = "t4", title = "Upload Package Images", link = "/vendor/my-services" });
                    }
                }
            }

            return Ok(tasks);
        }

        [Authorize(Policy = "Vendor")]
        [HttpGet("analytics")]
        public async Task<IActionResult> GetAnalytics()
        {
            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null)
            {
                return NotFound(new { error = "Vendor not found" });
            }

            // Fetch bookings for this vendor
            var bookings = await _db.Bookings.Where(b => b.VendorId == vendor.Id).ToListAsync();

            var totalEarnings = bookings
                .Where(b => b.Status.ToLower() == "paid" || b.Status.ToLower() == "confirmed" || b.Status.ToLower() == "completed" || b.Status.ToLower() == "settled")
                .Sum(b => b.TotalAmount);

            // Group bookings by status
            var bookingCountByStatus = new Dictionary<string, int>
            {
                { "pending", 0 },
                { "accepted", 0 },
                { "declined", 0 },
                { "completed", 0 }
            };

            foreach (var b in bookings)
            {
                var status = b.Status.ToLower();
                if (status == "pending") bookingCountByStatus["pending"]++;
                else if (status == "confirmed" || status == "paid") bookingCountByStatus["accepted"]++;
                else if (status == "cancelled" || status == "rejected") bookingCountByStatus["declined"]++;
                else if (status == "completed" || status == "settled") bookingCountByStatus["completed"]++;
            }

            // Monthly earnings for the current year
            var monthlyEarnings = new decimal[12];
            var currentYear = DateTime.UtcNow.Year;
            foreach (var b in bookings)
            {
                if (b.EventDate.Year == currentYear && (b.Status.ToLower() == "paid" || b.Status.ToLower() == "confirmed" || b.Status.ToLower() == "completed" || b.Status.ToLower() == "settled"))
                {
                    int monthIdx = b.EventDate.Month - 1;
                    if (monthIdx >= 0 && monthIdx < 12)
                    {
                        monthlyEarnings[monthIdx] += b.TotalAmount;
                    }
                }
            }

            // Fetch average rating trend
            var reviews = await _db.Reviews.Where(r => r.VendorId == vendor.Id && r.Status != "removed").ToListAsync();
            
            // Average rating trend, flat at the current average until per-month history exists.
            // A vendor with no reviews has no rating. Showing 4.8 invented one.
            var avgRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 1) : 0d;
            var averageRatingTrend = Enumerable.Repeat((double)avgRating, 12).ToArray();

            return Ok(new
            {
                totalEarnings,
                monthlyEarnings = monthlyEarnings.Select(e => (double)e).ToArray(),
                bookingCountByStatus,
                averageRatingTrend
            });
        }
    }
}
