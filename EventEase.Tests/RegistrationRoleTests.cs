using System.Net;
using System.Net.Http.Json;
using EventEase.Core.Constants;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// Registration wrote the posted role straight onto the new account, so
    /// posting role "Admin" to the open, unauthenticated sign-up endpoint
    /// created a full administrator: the whole admin surface, every customer
    /// record, every payout.
    /// </summary>
    /// <remarks>
    /// Deliberately small. /auth/register is rate limited to ten calls a
    /// minute and every case here shares one partition, so this class stays
    /// well under that; the rest of the endpoint tests seed accounts directly
    /// (see <see cref="ApiTestBase"/>).
    /// </remarks>
    public class RegistrationRoleTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public RegistrationRoleTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private static object Registration(string email, string? role) => new
        {
            name = "Someone",
            email,
            password = "Str0ng!Passw0rd",
            phone = "9999999999",
            role
        };

        private async Task<EventEase.Core.Entities.User?> UserFor(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
            return await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        }

        [Theory]
        [InlineData(AuthRoles.Admin)]
        [InlineData(AuthRoles.Support)]
        [InlineData(AuthRoles.Finance)]
        public async Task A_stranger_cannot_sign_themselves_up_as_staff(string role)
        {
            var email = $"escalate.{Guid.NewGuid():N}@example.test";

            var res = await _client.PostAsJsonAsync("/api/v1/auth/register", Registration(email, role));

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Null(await UserFor(email));
        }

        [Fact]
        public async Task The_refusal_does_not_name_the_roles_that_do_exist()
        {
            var email = $"escalate.{Guid.NewGuid():N}@example.test";

            var res = await _client.PostAsJsonAsync("/api/v1/auth/register", Registration(email, "Admin"));
            var body = await res.Content.ReadAsStringAsync();

            Assert.Contains("cannot be created through sign-up", body);
            Assert.DoesNotContain("Support", body);
            Assert.DoesNotContain("Vendor", body);
        }

        [Fact]
        public async Task Signing_up_as_a_vendor_still_works_whatever_the_casing()
        {
            var email = $"vendor.{Guid.NewGuid():N}@example.test";

            var res = await _client.PostAsJsonAsync("/api/v1/auth/register", Registration(email, "vendor"));
            res.EnsureSuccessStatusCode();

            var user = await UserFor(email);
            Assert.NotNull(user);
            Assert.Equal(AuthRoles.Vendor, user!.Role); // stored in canonical casing
        }

        [Fact]
        public async Task Asking_for_no_role_still_gets_a_customer_account()
        {
            var email = $"customer.{Guid.NewGuid():N}@example.test";

            var res = await _client.PostAsJsonAsync("/api/v1/auth/register", Registration(email, null));
            res.EnsureSuccessStatusCode();

            Assert.Equal(AuthRoles.Customer, (await UserFor(email))!.Role);
        }
    }
}
