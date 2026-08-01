using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;

namespace EventEase.Api.Controllers
{
    public static class PackageRatingHelper
    {
        public static async Task RecalculatePackageRating(EventEaseDbContext db, Guid bookingId)
        {
            var booking = await db.Bookings.FindAsync(bookingId);
            if (booking != null && booking.PackageId.HasValue)
            {
                var packageId = booking.PackageId.Value;
                
                var stats = await (from b in db.Bookings
                                    join r in db.Reviews on b.Id equals r.BookingId
                                    where b.PackageId == packageId && r.Status == "published"
                                    select new { r.Rating })
                                   .ToListAsync();

                var package = await db.Packages.FindAsync(packageId);
                if (package != null)
                {
                    package.TotalReviews = stats.Count;
                    package.Rating = stats.Count > 0 ? Math.Round(stats.Average(x => x.Rating), 1) : 0.0;
                    db.Packages.Update(package);
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
