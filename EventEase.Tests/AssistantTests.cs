using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EventEase.Application.Auth;
using EventEase.Core.Constants;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// Roshi answers from the customer's own data and the platform's real rules (rules engine; the
    /// Claude path is switched off in tests).
    /// </summary>
    public class AssistantTests : IClassFixture<RelationalTestWebApplicationFactory>
    {
        private readonly RelationalTestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        private const string Description =
            "Package.\n\n---INCLUSION_DETAILS---\n{\"Venue\":{\"minPrice\":50000,\"maxPrice\":50000}}";

        public AssistantTests(RelationalTestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _factory.EnsureDatabase();
        }

        [Fact]
        public async Task Welcome_NamesTheCustomersNextEvent()
        {
            var (_, customer) = await CreateUserAsync(AuthRoles.User, "Asha Rao");
            var package = await CreatePackageAsync("Lakeside Wedding", "Hyderabad", 250000m, 300);
            await BookAsync(customer, package.Id, "Sister wedding", 20, pay: true);

            var welcome = await SendAsync(HttpMethod.Get, "/api/v1/assistant/welcome", customer);
            Assert.Equal(HttpStatusCode.OK, welcome.Status);
            var reply = welcome.Body.GetProperty("reply").GetString()!;
            Assert.Contains("Hi Asha!", reply);
            Assert.Contains("Sister wedding", reply);
            Assert.Contains("in 20 days", reply);
            Assert.Equal("bookings", welcome.Body.GetProperty("cards")[0].GetProperty("type").GetString());
            Assert.Equal("rules", welcome.Body.GetProperty("poweredBy").GetString());
        }

        [Fact]
        public async Task PackageSearch_UnderstandsCityBudgetAndGuests()
        {
            var (_, customer) = await CreateUserAsync(AuthRoles.User, "Asha");
            var city = "Vizag" + Guid.NewGuid().ToString("N")[..4];
            var fits = await CreatePackageAsync("Beach Wedding", city, 280000m, 250);
            await CreatePackageAsync("Palace Wedding", city, 900000m, 250);   // over budget
            await CreatePackageAsync("Small Hall", city, 150000m, 80);          // too small

            var answer = await ChatAsync(customer, $"wedding venues in {city} under 3 lakh for 200 guests");
            var packages = answer.GetProperty("cards")[0].GetProperty("packages");
            Assert.Equal(1, packages.GetArrayLength());
            Assert.Equal($"pkg_{fits.Id:N}", packages[0].GetProperty("id").GetString());
            Assert.Equal("Garden Events", packages[0].GetProperty("vendorName").GetString());
            var reply = answer.GetProperty("reply").GetString()!;
            Assert.Contains("under ₹3,00,000", reply);
            Assert.Contains("for 200 guests", reply);
        }

        [Fact]
        public async Task Bookings_ShowOnlyTheCustomersOwn_WithWhatIsDue()
        {
            var (_, customer) = await CreateUserAsync(AuthRoles.User, "Asha");
            var (_, other) = await CreateUserAsync(AuthRoles.User, "Bina");
            var package = await CreatePackageAsync("Lakeside Wedding", "Hyderabad", 250000m, 300);
            await BookAsync(customer, package.Id, "Reception", 45, pay: true);
            await BookAsync(customer, package.Id, "Engagement", 60, pay: false);
            await BookAsync(other, package.Id, "Someone else's party", 30, pay: true);

            var answer = await ChatAsync(customer, "show my bookings");
            var bookings = answer.GetProperty("cards")[0].GetProperty("bookings");
            var names = bookings.EnumerateArray().Select(b => b.GetProperty("eventName").GetString()).ToList();
            Assert.Equal(new[] { "Reception", "Engagement" }, names);
            Assert.Contains("1 is waiting for payment", answer.GetProperty("reply").GetString());
            var reception = bookings[0];
            Assert.Equal("advance_paid", reception.GetProperty("status").GetString());
            Assert.True(reception.GetProperty("amountPaid").GetDecimal() > 0);
            Assert.Equal(reception.GetProperty("totalAmount").GetDecimal() - reception.GetProperty("amountPaid").GetDecimal(),
                reception.GetProperty("balanceDue").GetDecimal());

            var due = await ChatAsync(customer, "how much do I still owe?");
            Assert.Contains("due across 2 bookings", due.GetProperty("reply").GetString());
        }

        [Fact]
        public async Task Refunds_UseTheRealScheduleAndTheCustomersNextBooking()
        {
            var (_, customer) = await CreateUserAsync(AuthRoles.User, "Asha");
            var package = await CreatePackageAsync("Lakeside Wedding", "Hyderabad", 250000m, 300);
            var bookingId = await BookAsync(customer, package.Id, "Sangeet", 20, pay: true);

            var answer = await ChatAsync(customer, "if I cancel will I get a refund?");
            var reply = answer.GetProperty("reply").GetString()!;
            Assert.Contains("15–30 days before: 50% of the advance", reply);
            Assert.Contains("For your **Sangeet**", reply);
            Assert.Contains("50% of the advance", reply);
            Assert.Equal($"booking:{bookingId}", answer.GetProperty("actions")[0].GetProperty("target").GetString());
        }

        [Fact]
        public async Task Rewards_ComeFromTheLoyaltyLedger()
        {
            var (_, customer) = await CreateUserAsync(AuthRoles.User, "Asha");
            var package = await CreatePackageAsync("Lakeside Wedding", "Hyderabad", 250000m, 300);
            await BookAsync(customer, package.Id, "Reception", 45, pay: true);

            var answer = await ChatAsync(customer, "how many reward points do I have");
            var rewards = answer.GetProperty("cards")[0].GetProperty("rewards");
            Assert.True(rewards.GetProperty("points").GetInt32() > 0);
            Assert.Contains("10 points for every ₹100", answer.GetProperty("reply").GetString());
        }

        [Fact]
        public async Task Chat_RequiresSignInAndTheCustomersMessage()
        {
            var anonymous = await _client.PostAsJsonAsync("/api/v1/assistant/chat", new { messages = new[] { new { role = "user", content = "hi" } } });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            var (_, customer) = await CreateUserAsync(AuthRoles.User, "Asha");
            var empty = await SendAsync(HttpMethod.Post, "/api/v1/assistant/chat", customer, new { messages = Array.Empty<object>() });
            Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        }

        private async Task<JsonElement> ChatAsync(string token, string message)
        {
            var result = await SendAsync(HttpMethod.Post, "/api/v1/assistant/chat", token, new
            {
                messages = new[] { new { role = "assistant", content = "Hi! I'm Roshi." }, new { role = "user", content = message } }
            });
            Assert.Equal(HttpStatusCode.OK, result.Status);
            return result.Body;
        }

        private async Task<string> BookAsync(string customer, Guid packageId, string eventName, int daysAhead, bool pay)
        {
            var created = await SendAsync(HttpMethod.Post, "/api/v1/booking", customer, new
            {
                packageId = packageId.ToString(),
                eventDate = DateTime.UtcNow.Date.AddDays(daysAhead),
                guestCount = 80,
                eventName
            });
            Assert.Equal(HttpStatusCode.OK, created.Status);
            var bookingId = created.Body.GetProperty("id").GetString()!;
            if (pay)
            {
                var initiated = await SendAsync(HttpMethod.Post, "/api/v1/payment/initiate", customer, new { bookingId, paymentMethod = "UPI" });
                await SendAsync(HttpMethod.Post, "/api/v1/payment/confirm", customer, new { providerRef = initiated.Body.GetProperty("providerRef").GetString() });
            }
            return bookingId;
        }

        private async Task<(Guid Id, string Token)> CreateUserAsync(string role, string name)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
            var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@test.com", Name = name, Role = role, Phone = "9999999999" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return (user.Id, scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user.Id, role));
        }

        private async Task<Package> CreatePackageAsync(string name, string city, decimal price, int maxGuests)
        {
            var (userId, _) = await CreateUserAsync(AuthRoles.Vendor, "Ravi");
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
            var vendor = new Vendor { Id = Guid.NewGuid(), UserId = userId, BusinessName = "Garden Events", Description = "Events", IsValidated = true };
            var package = new Package
            {
                Id = Guid.NewGuid(),
                VendorId = vendor.Id,
                Category = "wedding",
                Name = name,
                Description = Description,
                IsActive = true,
                IsVerified = true,
                Pricing = new PackagePricing { BasePrice = price },
                Capacity = new PackageCapacity { MaxGuests = maxGuests },
                Address = new PackageAddress { Street = "Lake Road", City = city }
            };
            db.Vendors.Add(vendor);
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package;
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
