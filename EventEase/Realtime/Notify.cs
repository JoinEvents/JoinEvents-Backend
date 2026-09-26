using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Api.Realtime
{
    /// <summary>
    /// Adds notifications to the context; the caller's SaveChanges stores them, and
    /// NotificationPushInterceptor then delivers each one live and to the recipient's phone.
    /// </summary>
    public static class Notify
    {
        /// <summary>Notification types, which also decide where a tapped push opens (PushLinks).</summary>
        public const string Booking = "booking";
        public const string Verification = "verification";
        public const string Support = "support";
        public const string Dispute = "dispute";
        public const string Quote = "rfp";
        public const string General = "general";

        public static void User(EventEaseDbContext db, Guid userId, string title, string message, string type)
        {
            if (userId == Guid.Empty) return;
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                CreatedAt = DateTime.UtcNow
            });
        }

        /// <summary>Everyone who works the queues: all support agents and admins.</summary>
        public static async Task StaffAsync(EventEaseDbContext db, string title, string message, string type, Guid? except = null)
        {
            var staff = await db.Users.AsNoTracking()
                .Where(u => u.Role == AuthRoles.Support || u.Role == AuthRoles.Admin)
                .Select(u => u.Id)
                .ToListAsync();
            foreach (var id in staff.Where(id => id != except))
                User(db, id, title, message, type);
        }

        /// <summary>The user behind a vendor id (a vendor profile id, or older rows' vendor user id).</summary>
        public static async Task<Guid> VendorUserIdAsync(EventEaseDbContext db, Guid vendorOrUserId) =>
            await db.Vendors.AsNoTracking()
                .Where(v => v.Id == vendorOrUserId || v.UserId == vendorOrUserId)
                .Select(v => v.UserId)
                .FirstOrDefaultAsync();
    }
}
