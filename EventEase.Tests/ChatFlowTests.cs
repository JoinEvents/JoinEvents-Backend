using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EventEase.Application.Auth;
using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// Once a vendor confirms a booking, customer and vendor can talk: the conversation opens with
    /// the booking's details, messages flow both ways, and both sides get events live over the hub.
    /// </summary>
    public class ChatFlowTests : IClassFixture<RelationalTestWebApplicationFactory>
    {
        private readonly RelationalTestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        private const string Description =
            "Package.\n\n---INCLUSION_DETAILS---\n{\"Venue\":{\"minPrice\":50000,\"maxPrice\":50000}}";

        public ChatFlowTests(RelationalTestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _factory.EnsureDatabase();
        }

        [Fact]
        public async Task ConfirmingABooking_OpensTheConversation_AndMessagesFlowLive()
        {
            var (customerId, customer) = await CreateUserAsync(AuthRoles.User);
            var (vendorUserId, vendor, package) = await CreateVendorWithPackageAsync();

            // The customer is connected before anything happens, as an open app would be.
            await using var customerHub = await ConnectAsync(customer);
            var customerEvents = Record(customerHub);
            await using var vendorHub = await ConnectAsync(vendor);
            var vendorEvents = Record(vendorHub);

            var bookingId = await BookAndPayAsync(customer, package.Id);

            // The vendor hears about the paid booking the moment it is paid.
            var paid = await vendorEvents.NextAsync("Notification");
            Assert.Equal("New booking paid", paid.GetProperty("title").GetString());

            var confirmed = await SendAsync(HttpMethod.Patch, $"/api/v1/bookings/{bookingId}/status", vendor, new { status = "confirmed" });
            Assert.Equal(HttpStatusCode.OK, confirmed.Status);
            var threadId = confirmed.Body.GetProperty("threadId").GetString()!;

            // Both sides get the opening message live, and the customer the confirmation notice.
            var opening = await customerEvents.NextAsync("ReceiveMessage");
            Assert.Equal(threadId, opening.GetProperty("threadId").GetString());
            Assert.Contains("is confirmed", opening.GetProperty("content").GetString());
            await vendorEvents.NextAsync("ReceiveMessage");
            var notice = await customerEvents.NextAsync("Notification");
            Assert.Equal("Booking confirmed", notice.GetProperty("title").GetString());

            // The conversation is listed for both, open and titled with the event.
            var threads = await SendAsync(HttpMethod.Get, "/api/v1/messenger/threads", customer);
            var thread = threads.Body.EnumerateArray().Single(t => t.GetProperty("ThreadId").GetString() == threadId);
            Assert.Equal("Active", thread.GetProperty("Status").GetString());
            Assert.Equal("Garden Wedding", thread.GetProperty("EventTitle").GetString());
            var alive = await SendAsync(HttpMethod.Get, $"/api/v1/messenger/threads/{threadId}/alive", customer);
            Assert.True(alive.Body.GetProperty("isAlive").GetBoolean());

            // The customer writes (the web app sends "Content", the mobile app "body"); the vendor
            // receives each live and can read the history.
            var sent = await SendAsync(HttpMethod.Post, $"/api/v1/messenger/threads/{threadId}/messages", customer, new { Content = "Can we start at 6?" });
            Assert.Equal(HttpStatusCode.Created, sent.Status);
            Assert.Equal("Can we start at 6?", (await vendorEvents.NextAsync("ReceiveMessage")).GetProperty("content").GetString());

            var mobile = await SendAsync(HttpMethod.Post, $"/api/v1/messenger/threads/{threadId}/messages", customer, new { body = "And 120 guests." });
            Assert.Equal(HttpStatusCode.Created, mobile.Status);
            Assert.Equal("And 120 guests.", (await vendorEvents.NextAsync("ReceiveMessage")).GetProperty("content").GetString());

            var reply = await SendAsync(HttpMethod.Post, $"/api/v1/messenger/threads/{threadId}/messages", vendor, new { Content = "Yes, 6 PM works." });
            Assert.Equal(HttpStatusCode.Created, reply.Status);
            // Messages go to both parties (the sender's other devices stay in step), so the customer
            // also sees their own two before the vendor's reply.
            var seenByCustomer = new List<string>();
            for (var i = 0; i < 3; i++) seenByCustomer.Add((await customerEvents.NextAsync("ReceiveMessage")).GetProperty("content").GetString()!);
            Assert.Equal(new[] { "Can we start at 6?", "And 120 guests.", "Yes, 6 PM works." }, seenByCustomer);

            var history = await SendAsync(HttpMethod.Get, $"/api/v1/messenger/threads/{threadId}/messages", vendor);
            Assert.Equal(4, history.Body.GetArrayLength());

            // The booking being paid does not close it (it used to, for quote-request threads).
            Assert.True((await SendAsync(HttpMethod.Get, $"/api/v1/messenger/threads/{threadId}/alive", vendor)).Body.GetProperty("isAlive").GetBoolean());
            Assert.NotEqual(customerId, vendorUserId);
        }

        [Fact]
        public async Task Strangers_CannotReadOrAnswerAConversation()
        {
            var (_, customer) = await CreateUserAsync(AuthRoles.User);
            var (_, vendor, package) = await CreateVendorWithPackageAsync();
            var (_, stranger) = await CreateUserAsync(AuthRoles.User);
            var (_, otherVendor, _) = await CreateVendorWithPackageAsync();

            var bookingId = await BookAndPayAsync(customer, package.Id);
            var confirmed = await SendAsync(HttpMethod.Patch, $"/api/v1/bookings/{bookingId}/status", vendor, new { status = "confirmed" });
            var threadId = confirmed.Body.GetProperty("threadId").GetString()!;

            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, $"/api/v1/messenger/threads/{threadId}/messages", stranger)).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, $"/api/v1/messenger/threads/{threadId}/messages", stranger, new { Content = "hi" })).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/api/v1/messenger/threads/{threadId}/reject", otherVendor)).Status);
            // Even the customer cannot answer the vendor's side of a request.
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/api/v1/messenger/threads/{threadId}/reject", customer)).Status);
        }

        [Fact]
        public async Task CustomerCanStartAChat_WithTheVendorIdInTheBody()
        {
            var (_, customer) = await CreateUserAsync(AuthRoles.User);
            var (_, vendor, package) = await CreateVendorWithPackageAsync();

            var requested = await SendAsync(HttpMethod.Post, "/api/v1/messenger/request", customer,
                new { vendorId = $"usr_{package.VendorId:N}", message = "Is 12 Dec free?" });
            Assert.Equal(HttpStatusCode.OK, requested.Status);
            var threadId = requested.Body.GetProperty("threadId").GetString()!;

            var vendorThreads = await SendAsync(HttpMethod.Get, "/api/v1/messenger/threads", vendor);
            Assert.Contains(vendorThreads.Body.EnumerateArray(), t => t.GetProperty("ThreadId").GetString() == threadId);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/v1/messenger/threads/{threadId}/accept", vendor)).Status);
        }

        [Fact]
        public async Task Notifications_CanBeMarkedReadOneByOneAndCleared()
        {
            var (_, customer) = await CreateUserAsync(AuthRoles.User);
            var (_, vendor, package) = await CreateVendorWithPackageAsync();
            var bookingId = await BookAndPayAsync(customer, package.Id);
            await SendAsync(HttpMethod.Patch, $"/api/v1/bookings/{bookingId}/status", vendor, new { status = "confirmed" });

            var list = await SendAsync(HttpMethod.Get, "/api/v1/notifications", customer);
            var id = list.Body.EnumerateArray().First().GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Patch, $"/api/v1/notifications/{id}/read", customer)).Status);
            var after = await SendAsync(HttpMethod.Get, "/api/v1/notifications", customer);
            Assert.True(after.Body.EnumerateArray().Single(n => n.GetProperty("id").GetString() == id).GetProperty("isRead").GetBoolean());

            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, "/api/v1/notifications/read-all", customer)).Status);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Delete, "/api/v1/notifications", customer)).Status);
            Assert.Equal(0, (await SendAsync(HttpMethod.Get, "/api/v1/notifications", customer)).Body.GetArrayLength());
        }

        // ---- helpers ----------------------------------------------------------------------

        private async Task<HubConnection> ConnectAsync(string token)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(_client.BaseAddress!, "/hubs/chat"), options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                })
                .Build();
            await connection.StartAsync();
            return connection;
        }

        private static EventRecorder Record(HubConnection connection)
        {
            var recorder = new EventRecorder();
            connection.On<JsonElement>("ReceiveMessage", e => recorder.Add("ReceiveMessage", e));
            connection.On<JsonElement>("Notification", e => recorder.Add("Notification", e));
            return recorder;
        }

        private sealed class EventRecorder
        {
            private readonly ConcurrentDictionary<string, BlockingCollection<JsonElement>> _events = new();

            public void Add(string name, JsonElement payload) => Queue(name).Add(payload.Clone());

            public Task<JsonElement> NextAsync(string name) => Task.Run(() =>
                Queue(name).TryTake(out var item, TimeSpan.FromSeconds(10))
                    ? item
                    : throw new TimeoutException($"No '{name}' event arrived."));

            private BlockingCollection<JsonElement> Queue(string name) => _events.GetOrAdd(name, _ => new BlockingCollection<JsonElement>());
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
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = $"{role.ToLowerInvariant()}_{Guid.NewGuid():N}@test.com",
                Name = role == AuthRoles.Vendor ? "Ravi" : "Asha",
                Role = role,
                Phone = "9999999999"
            };
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
                Id = Guid.NewGuid(),
                VendorId = vendor.Id,
                Category = "wedding",
                Name = "Garden Wedding",
                Description = Description,
                IsActive = true,
                IsVerified = true,
                Capacity = new PackageCapacity { MaxGuests = 300 },
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
