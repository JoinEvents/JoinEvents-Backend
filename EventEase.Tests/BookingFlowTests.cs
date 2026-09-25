using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// The customer journey end to end: price a package, book it with the catalogue's own ids,
    /// read the booking back and pay for it — the advance, or the whole amount.
    /// </summary>
    public class BookingFlowTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        // Catering ₹500/plate, Venue ₹50,000, Decor ₹20,000 — prices before GST.
        private const string Description =
            "Our wedding package.\n\n---INCLUSION_DETAILS---\n" +
            "{\"Catering\":{\"minPrice\":500,\"maxPrice\":500},\"Venue\":{\"minPrice\":50000,\"maxPrice\":50000},\"Decor\":{\"minPrice\":20000,\"maxPrice\":20000}}";

        public BookingFlowTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Quote_PricesCateringPerGuest_AndAddsGstOnTop()
        {
            var package = await SeedPackageAsync(maxGuests: 300);
            var token = await RegisterCustomerAsync();

            var quote = await SendAsync(HttpMethod.Post, "/api/v1/booking/quote", token,
                new { packageId = $"pkg_{package.Id:N}", guestCount = 200 });

            Assert.Equal(HttpStatusCode.OK, quote.Status);
            // 500 × 200 + 50,000 + 20,000 = 170,000; GST 18% = 30,600.
            Assert.Equal(170_000m, quote.Body.GetProperty("subtotal").GetDecimal());
            Assert.Equal(30_600m, quote.Body.GetProperty("gstAmount").GetDecimal());
            Assert.Equal(200_600m, quote.Body.GetProperty("totalAmount").GetDecimal());
            Assert.Equal(60_180m, quote.Body.GetProperty("advanceAmount").GetDecimal());
            Assert.Equal(3, quote.Body.GetProperty("lines").GetArrayLength());
        }

        [Fact]
        public async Task Quote_RejectsMoreGuestsThanThePackageCaters()
        {
            var package = await SeedPackageAsync(maxGuests: 150);
            var token = await RegisterCustomerAsync();

            var quote = await SendAsync(HttpMethod.Post, "/api/v1/booking/quote", token,
                new { packageId = package.Id.ToString(), guestCount = 151 });

            Assert.Equal(HttpStatusCode.BadRequest, quote.Status);
            Assert.Contains("150", quote.Body.GetProperty("error").GetString());
        }

        [Fact]
        public async Task Book_WithCatalogueIds_ThenPayAdvance_ThenBalance()
        {
            var package = await SeedPackageAsync(maxGuests: 300);
            var token = await RegisterCustomerAsync();

            var created = await SendAsync(HttpMethod.Post, "/api/v1/booking", token, new
            {
                packageId = $"pkg_{package.Id:N}",
                vendorId = $"usr_{package.VendorId:N}",
                eventDate = DateTime.UtcNow.Date.AddDays(40),
                guestCount = 200,
                eventName = "Asha's wedding"
            });

            Assert.Equal(HttpStatusCode.OK, created.Status);
            Assert.Equal(JsonValueKind.Object, created.Body.ValueKind);
            var bookingId = created.Body.GetProperty("id").GetString()!;
            Assert.Equal(200_600m, created.Body.GetProperty("totalAmount").GetDecimal());
            Assert.Equal("pending", created.Body.GetProperty("status").GetString());
            // Venue and city come from the package's own address when the customer leaves them out.
            Assert.Equal("Hyderabad", created.Body.GetProperty("city").GetString());
            Assert.Equal("Road No. 12, Banjara Hills, Hyderabad", created.Body.GetProperty("venue").GetString());
            Assert.Equal("wedding", created.Body.GetProperty("eventTypeId").GetString());

            var fetched = await SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", token);
            Assert.Equal(HttpStatusCode.OK, fetched.Status);
            Assert.Equal(bookingId, fetched.Body.GetProperty("id").GetString());

            // Advance.
            var advance = await PayAsync(token, bookingId, payInFull: false);
            Assert.Equal(60_180m, advance.Amount);
            var afterAdvance = await SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", token);
            Assert.Equal("confirmed", afterAdvance.Body.GetProperty("status").GetString());
            Assert.Equal(60_180m, afterAdvance.Body.GetProperty("amountPaid").GetDecimal());
            Assert.Equal(140_420m, afterAdvance.Body.GetProperty("balanceDue").GetDecimal());

            // Balance: the rest of the total, and the booking stays in its lifecycle.
            var balance = await PayAsync(token, bookingId, payInFull: false);
            Assert.Equal(140_420m, balance.Amount);
            var afterBalance = await SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", token);
            Assert.Equal(0m, afterBalance.Body.GetProperty("balanceDue").GetDecimal());
            Assert.Equal("confirmed", afterBalance.Body.GetProperty("status").GetString());

            var again = await SendAsync(HttpMethod.Post, "/api/v1/payment/initiate", token,
                new { bookingId, paymentMethod = "UPI" });
            Assert.Equal(HttpStatusCode.BadRequest, again.Status);
        }

        [Fact]
        public async Task Book_ThenPayInFull()
        {
            var package = await SeedPackageAsync(maxGuests: 300);
            var token = await RegisterCustomerAsync();

            var created = await SendAsync(HttpMethod.Post, "/api/v1/booking", token, new
            {
                packageId = package.Id.ToString(),
                eventDate = DateTime.UtcNow.Date.AddDays(60),
                guestCount = 100,
                venue = "Lakeside Lawn",
                city = "Secunderabad"
            });
            Assert.Equal(HttpStatusCode.OK, created.Status);
            var bookingId = created.Body.GetProperty("id").GetString()!;
            // The event name defaults to the package's own name.
            Assert.Equal("Royal Wedding", created.Body.GetProperty("eventName").GetString());
            Assert.Equal("Lakeside Lawn", created.Body.GetProperty("venue").GetString());

            // 500 × 100 + 70,000 = 120,000 + 18% = 141,600.
            var full = await PayAsync(token, bookingId, payInFull: true);
            Assert.Equal(141_600m, full.Amount);

            var after = await SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", token);
            Assert.Equal(0m, after.Body.GetProperty("balanceDue").GetDecimal());
            Assert.Equal(141_600m, after.Body.GetProperty("finalPaidAmount").GetDecimal());
        }

        [Fact]
        public async Task GetBooking_IsRefusedToOtherCustomers()
        {
            var package = await SeedPackageAsync(maxGuests: 300);
            var owner = await RegisterCustomerAsync();
            var stranger = await RegisterCustomerAsync();

            var created = await SendAsync(HttpMethod.Post, "/api/v1/booking", owner, new
            {
                packageId = package.Id.ToString(),
                eventDate = DateTime.UtcNow.Date.AddDays(20),
                guestCount = 50
            });
            var bookingId = created.Body.GetProperty("id").GetString()!;

            var fetched = await SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", stranger);
            Assert.Equal(HttpStatusCode.Forbidden, fetched.Status);
        }

        // ---- helpers ----------------------------------------------------------------------

        private async Task<(decimal Amount, string Status)> PayAsync(string token, string bookingId, bool payInFull)
        {
            var initiated = await SendAsync(HttpMethod.Post, "/api/v1/payment/initiate", token,
                new { bookingId, paymentMethod = "UPI", payInFull });
            Assert.Equal(HttpStatusCode.OK, initiated.Status);
            var providerRef = initiated.Body.GetProperty("providerRef").GetString();

            var confirmed = await SendAsync(HttpMethod.Post, "/api/v1/payment/confirm", token, new { providerRef });
            Assert.Equal(HttpStatusCode.OK, confirmed.Status);
            return (initiated.Body.GetProperty("amount").GetDecimal(), confirmed.Body.GetProperty("status").GetString()!);
        }

        private async Task<Package> SeedPackageAsync(int maxGuests)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();

            var vendorUser = new User
            {
                Id = Guid.NewGuid(),
                Email = $"vendor_{Guid.NewGuid():N}@test.com",
                Name = "Vendor",
                Role = "Vendor"
            };
            var vendor = new Vendor
            {
                Id = Guid.NewGuid(),
                UserId = vendorUser.Id,
                BusinessName = "Royal Banquets",
                Description = "Weddings",
                IsValidated = true
            };
            var package = new Package
            {
                Id = Guid.NewGuid(),
                VendorId = vendor.Id,
                Category = "wedding",
                Name = "Royal Wedding",
                Description = Description,
                IsActive = true,
                IsVerified = true,
                Capacity = new PackageCapacity { MaxGuests = maxGuests },
                Address = new PackageAddress { Street = "Road No. 12", Locality = "Banjara Hills", City = "Hyderabad" }
            };
            db.Users.Add(vendorUser);
            db.Vendors.Add(vendor);
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package;
        }

        private async Task<string> RegisterCustomerAsync()
        {
            var res = await _client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                name = "Customer",
                email = $"customer_{Guid.NewGuid():N}@test.com",
                password = "Password123!",
                phone = "9999999999",
                role = "User"
            });
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("token").GetString()!;
        }

        private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(
            HttpMethod method, string url, string token, object? payload = null)
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
