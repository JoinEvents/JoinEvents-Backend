using System.Net.Http.Headers;
using EventEase.Application.Auth;
using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// Shared plumbing for the endpoint tests: a seeded account of any role and
    /// a client already carrying its token.
    /// </summary>
    /// <remarks>
    /// Accounts are written straight to the database and tokens minted from the
    /// host's own <see cref="ITokenService"/> rather than going through
    /// /auth/register. Two reasons. The auth endpoints are rate limited to ten
    /// calls a minute per caller, and every test here shares one partition, so
    /// registering per test would start failing with 429 once a class grew past
    /// ten cases. And admin and support accounts can no longer be registered
    /// for at all — which is the point of <see cref="RegistrationRoleTests"/> —
    /// so a staff caller has to be seeded regardless.
    /// </remarks>
    public abstract class ApiTestBase : IClassFixture<TestWebApplicationFactory>
    {
        protected readonly TestWebApplicationFactory Factory;

        protected ApiTestBase(TestWebApplicationFactory factory)
        {
            Factory = factory;
        }

        /// <summary>Runs a block against the application's own database.</summary>
        protected async Task WithDb(Func<EventEaseDbContext, Task> work)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
            await work(db);
        }

        protected async Task<T> WithDb<T>(Func<EventEaseDbContext, Task<T>> work)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
            return await work(db);
        }

        /// <summary>Creates an account of the given role and returns it.</summary>
        protected async Task<User> SeedUser(string role, string? name = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Name = name ?? $"{role} {Guid.NewGuid().ToString("N")[..6]}",
                Email = $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@example.test",
                Phone = "9999999999",
                Role = role,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Passw0rd", workFactor: 4),
                CreatedAt = DateTime.UtcNow
            };

            await WithDb(async db =>
            {
                db.Users.Add(user);
                await db.SaveChangesAsync();
            });

            return user;
        }

        /// <summary>A vendor account together with the vendor row bookings point at.</summary>
        protected async Task<(User user, Vendor vendor)> SeedVendor(string businessName = "Perfect Weddings Co")
        {
            var user = await SeedUser(AuthRoles.Vendor);
            var vendor = new Vendor
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                BusinessName = businessName,
                Description = "Vendor partner offering event services.",
                Location = "Bangalore",
                IsValidated = true
            };

            await WithDb(async db =>
            {
                db.Vendors.Add(vendor);
                await db.SaveChangesAsync();
            });

            return (user, vendor);
        }

        protected async Task<Booking> SeedBooking(Guid customerId, Guid vendorId, Action<Booking>? adjust = null)
        {
            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                UserId = customerId,
                VendorId = vendorId,
                EventDate = DateTime.UtcNow.AddDays(30),
                Status = "Paid",
                Amount = 100000m,
                TotalAmount = 118000m,
                AdvanceAmount = 30000m,
                PlatformFeeAmount = 11800m,
                TdsDeducted = 1180m,
                VendorPayoutAmount = 105020m,
                EventName = "Reception",
                Venue = "Hotel Banquet",
                City = "Mumbai"
            };

            adjust?.Invoke(booking);

            await WithDb(async db =>
            {
                db.Bookings.Add(booking);
                await db.SaveChangesAsync();
            });

            return booking;
        }

        /// <summary>A client signed in as the given account.</summary>
        protected HttpClient ClientFor(User user)
        {
            using var scope = Factory.Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var token = tokens.CreateAccessToken(user.Id, user.Role);

            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
    }
}
