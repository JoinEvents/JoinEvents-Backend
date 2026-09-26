using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EventEase.Api.Push;
using EventEase.Application.Auth;
using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>Records what would go to phones instead of calling Firebase.</summary>
    public class FakePushSender : IPushSender
    {
        public record Sent(Guid UserId, string Role, string Title, string Body, string Kind, string Link, string? Group = null);
        public readonly ConcurrentQueue<Sent> Items = new();
        private readonly IServiceScopeFactory _scopes;
        public FakePushSender(IServiceScopeFactory scopes) => _scopes = scopes;
        public bool Enabled => true;

        public Task SendAsync(IReadOnlyCollection<Guid> userIds, PushMessage message)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
            foreach (var id in userIds)
            {
                var role = db.Users.Where(u => u.Id == id).Select(u => u.Role).FirstOrDefault() ?? "";
                Items.Enqueue(new Sent(id, role, message.Title, message.Body, message.Kind, message.Link(PushLinks.Area(role)), message.Group));
            }
            return Task.CompletedTask;
        }

        /// <summary>Pushes are sent in the background; wait for the one expected.</summary>
        public async Task<Sent> WaitForAsync(Func<Sent, bool> match)
        {
            for (var i = 0; i < 100; i++)
            {
                var hit = Items.FirstOrDefault(match);
                if (hit != null) return hit;
                await Task.Delay(30);
            }
            throw new Xunit.Sdk.XunitException("Expected push was not sent. Sent: " + string.Join(" | ", Items.Select(x => $"{x.Role}:{x.Title}")));
        }
    }

    public class PushTestFactory : RelationalTestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<FakePushSender>();
                services.AddSingleton<IPushSender>(sp => sp.GetRequiredService<FakePushSender>());
            });
        }

        public FakePushSender Push => Services.GetRequiredService<FakePushSender>();
    }

    /// <summary>
    /// Every role hears about what concerns it, in the app and on the phone, and a tapped push
    /// opens that role's screen.
    /// </summary>
    public class NotificationPushTests : IClassFixture<PushTestFactory>
    {
        private readonly PushTestFactory _factory;
        private readonly HttpClient _client;

        private const string Description =
            "Package.\n\n---INCLUSION_DETAILS---\n{\"Venue\":{\"minPrice\":50000,\"maxPrice\":50000}}";

        public NotificationPushTests(PushTestFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _factory.EnsureDatabase();
        }

        [Theory]
        [InlineData("User", "booking", null, "/customer/tabs/bookings")]
        [InlineData("Vendor", "booking", null, "/vendor/tabs/bookings")]
        [InlineData("Support", "verification", null, "/support/tabs/verifications")]
        [InlineData("Admin", "dispute", null, "/admin/disputes")]
        [InlineData("Vendor", "message", "t1", "/vendor/chat/t1")]
        [InlineData("Customer", "support", null, "/customer/support")]
        [InlineData("Admin", "general", null, "/admin/notifications")]
        public void TappedPush_OpensTheRolesScreen(string role, string kind, string? id, string expected) =>
            Assert.Equal(expected, PushLinks.For(role, kind, id));

        [Fact]
        public async Task DeviceTokens_RegisterForAnyRole_FollowTheLastSignIn_AndUnregister()
        {
            var (supportId, support) = await CreateUserAsync(AuthRoles.Support);
            var (vendorId, vendor) = await CreateUserAsync(AuthRoles.Vendor);
            var token = "fcm-" + Guid.NewGuid().ToString("N");

            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, "/api/v1/profile/device-token", support, new { token, platform = "android" })).Status);
            Assert.Equal(supportId, Owner(token));

            // The same phone, now signed in as someone else.
            await SendAsync(HttpMethod.Post, "/api/v1/profile/device-token", vendor, new { token, platform = "android" });
            Assert.Equal(vendorId, Owner(token));

            // Signing out elsewhere can't remove it; the owner's sign-out does.
            await SendAsync(HttpMethod.Delete, "/api/v1/profile/device-token", support, new { token });
            Assert.Equal(vendorId, Owner(token));
            await SendAsync(HttpMethod.Delete, "/api/v1/profile/device-token", vendor, new { token });
            Assert.Null(Owner(token));

            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, "/api/v1/profile/device-token", vendor, new { token = "" })).Status);
        }

        [Fact]
        public async Task SupportTickets_ReachAllStaff_AndRepliesReachTheCustomer()
        {
            var (customerId, customer) = await CreateUserAsync(AuthRoles.User);
            var (supportId, support) = await CreateUserAsync(AuthRoles.Support);
            var (adminId, _) = await CreateUserAsync(AuthRoles.Admin);
            var (_, stranger) = await CreateUserAsync(AuthRoles.User);

            var created = await SendAsync(HttpMethod.Post, "/api/v1/support/ticket", customer, new { subject = "Refund not received", description = "It has been a week." });
            Assert.Equal(HttpStatusCode.OK, created.Status);
            var ticketId = (created.Body.TryGetProperty("id", out var tid) ? tid : created.Body.GetProperty("Id")).ToString().Trim('"');

            // Every agent and admin, in the app and on the phone.
            Assert.Contains("New support ticket", Titles(supportId));
            Assert.Contains("New support ticket", Titles(adminId));
            Assert.Equal("/support/tabs/tickets", (await _factory.Push.WaitForAsync(p => p.UserId == supportId && p.Title == "New support ticket")).Link);
            Assert.Equal("/admin/notifications", (await _factory.Push.WaitForAsync(p => p.UserId == adminId && p.Title == "New support ticket")).Link);

            // Someone else's ticket can't be answered, and customers can't write staff notes.
            var guid = TicketGuid(ticketId);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, $"/api/v1/support/tickets/{guid}/reply", stranger, new { message = "hi", isInternal = false })).Status);

            // Support answers: the customer is told, with a link to Support.
            await SendAsync(HttpMethod.Post, $"/api/v1/support/tickets/{guid}/reply", support, new { message = "Refund is processing.", isInternal = false });
            var reply = await _factory.Push.WaitForAsync(p => p.UserId == customerId && p.Title.StartsWith("Support replied"));
            Assert.Equal("/customer/support", reply.Link);
            Assert.Equal("Refund is processing.", reply.Body);

            // An internal note tells nobody outside staff.
            await SendAsync(HttpMethod.Post, $"/api/v1/support/tickets/{guid}/reply", support, new { message = "check gateway", isInternal = true });
            await Task.Delay(200);
            Assert.DoesNotContain(_factory.Push.Items, p => p.UserId == customerId && p.Body == "check gateway");

            // Resolved: the customer hears.
            await SendAsync(HttpMethod.Patch, $"/api/v1/support/tickets/{guid}/status", support, new { status = "Resolved" });
            Assert.Contains("Your ticket is resolved ✅", Titles(customerId));
        }

        [Fact]
        public async Task ChatMessages_PushToTheOtherSide_NotTheSender()
        {
            var (customerId, customer) = await CreateUserAsync(AuthRoles.User);
            var (vendorUserId, vendor, package) = await CreateVendorWithPackageAsync();
            var bookingId = await BookAndPayAsync(customer, package.Id);

            // The vendor's phone buzzes for the paid booking and opens their bookings.
            Assert.Equal("/vendor/tabs/bookings", (await _factory.Push.WaitForAsync(p => p.UserId == vendorUserId && p.Title == "New booking paid")).Link);

            var confirmed = await SendAsync(HttpMethod.Patch, $"/api/v1/bookings/{bookingId}/status", vendor, new { status = "confirmed" });
            var threadId = confirmed.Body.GetProperty("threadId").GetString()!;

            await SendAsync(HttpMethod.Post, $"/api/v1/messenger/threads/{threadId}/messages", customer, new { Content = "Can we start at 6?" });
            var push = await _factory.Push.WaitForAsync(p => p.UserId == vendorUserId && p.Kind == "message" && p.Body == "Can we start at 6?");
            Assert.Equal($"/vendor/chat/{threadId}", push.Link);
            Assert.Equal("Asha", push.Title);
            Assert.Equal(threadId, push.Group); // one conversation, one group in the tray
            await Task.Delay(200);
            Assert.DoesNotContain(_factory.Push.Items, p => p.UserId == customerId && p.Body == "Can we start at 6?");
        }

        [Fact]
        public async Task Cancellations_AndDisputes_ReachTheOtherSide_AndStaff()
        {
            var (_, customer) = await CreateUserAsync(AuthRoles.User);
            var (supportId, _) = await CreateUserAsync(AuthRoles.Support);
            var (vendorUserId, vendor, package) = await CreateVendorWithPackageAsync();

            var cancelled = await BookAndPayAsync(customer, package.Id);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{cancelled}/cancel", customer, new { reason = "Plans changed" })).Status);
            Assert.Contains(Messages(vendorUserId), m => m.StartsWith("The customer cancelled"));

            var disputed = await BookAndPayAsync(customer, package.Id);
            await SendAsync(HttpMethod.Patch, $"/api/v1/bookings/{disputed}/status", vendor, new { status = "confirmed" });
            await SendAsync(HttpMethod.Patch, $"/api/v1/bookings/{disputed}/status", vendor, new { status = "in_progress" });
            await SendAsync(HttpMethod.Patch, $"/api/v1/bookings/{disputed}/status", vendor, new { status = "completed" });
            var raised = await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{disputed}/dispute", customer, new { reason = "Decor missing" });
            Assert.Equal(HttpStatusCode.OK, raised.Status);
            Assert.Equal("/support/tabs/bookings", (await _factory.Push.WaitForAsync(p => p.UserId == supportId && p.Title == "Dispute raised")).Link);
            Assert.Contains("A customer raised a dispute", Titles(vendorUserId));
        }

        // ---- helpers ------------------------------------------------------------------------

        private Guid? Owner(string token)
        {
            using var scope = _factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<EventEaseDbContext>().DeviceTokens
                .Where(t => t.Token == token).Select(t => (Guid?)t.UserId).FirstOrDefault();
        }

        private List<string> Titles(Guid userId) => Rows(userId).Select(n => n.Title).ToList();
        private List<string> Messages(Guid userId) => Rows(userId).Select(n => n.Message).ToList();
        private List<Notification> Rows(Guid userId)
        {
            using var scope = _factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<EventEaseDbContext>().Notifications.Where(n => n.UserId == userId).ToList();
        }

        private static Guid TicketGuid(string id)
        {
            var raw = id.Contains('_') ? id[(id.IndexOf('_') + 1)..] : id;
            return Guid.Parse(raw);
        }

        private async Task<string> BookAndPayAsync(string customer, Guid packageId)
        {
            var created = await SendAsync(HttpMethod.Post, "/api/v1/booking", customer, new
            {
                packageId = packageId.ToString(),
                eventDate = DateTime.UtcNow.Date.AddDays(40 + Random.Shared.Next(300)),
                guestCount = 80
            });
            Assert.Equal(HttpStatusCode.OK, created.Status);
            var bookingId = created.Body.GetProperty("id").GetString()!;
            var initiated = await SendAsync(HttpMethod.Post, "/api/v1/payment/initiate", customer, new { bookingId, paymentMethod = "UPI" });
            await SendAsync(HttpMethod.Post, "/api/v1/payment/confirm", customer, new { providerRef = initiated.Body.GetProperty("providerRef").GetString() });
            return bookingId;
        }

        private async Task<(Guid Id, string Token)> CreateUserAsync(string role)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
            var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@test.com", Name = role == AuthRoles.Vendor ? "Ravi" : "Asha", Role = role, Phone = "9999999999" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return (user.Id, scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user.Id, role));
        }

        private async Task<(Guid UserId, string Token, Package Package)> CreateVendorWithPackageAsync()
        {
            var (userId, token) = await CreateUserAsync(AuthRoles.Vendor);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
            var vendor = new Vendor { Id = Guid.NewGuid(), UserId = userId, BusinessName = "Garden Events", Description = "Events", IsValidated = true };
            var package = new Package
            {
                Id = Guid.NewGuid(), VendorId = vendor.Id, Category = "wedding", Name = "Garden Wedding", Description = Description,
                IsActive = true, IsVerified = true, Capacity = new PackageCapacity { MaxGuests = 300 },
                Address = new PackageAddress { Street = "Lake Road", City = "Hyderabad" }
            };
            db.Vendors.Add(vendor);
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return (userId, token, package);
        }

        private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string url, string token, object? payload = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            var response = await _client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            var body = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
            return (response.StatusCode, body);
        }
    }
}
