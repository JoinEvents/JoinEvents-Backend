using EventEase.Application.Auth;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;
using static EventEase.Application.Auth.Dtos;

namespace EventEase.Tests
{
    /// <summary>
    /// Refresh tokens were previously stored in plaintext, never returned to the client and never
    /// redeemable. These tests pin the rotation and reuse-detection behaviour.
    /// </summary>
    public class RefreshTokenTests : IDisposable
    {
        private readonly EventEaseDbContext _db;
        private readonly AuthService _auth;

        public RefreshTokenTests()
        {
            var options = new DbContextOptionsBuilder<EventEaseDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            _db = new EventEaseDbContext(options);
            _db.Database.EnsureCreated();

            var jwt = Options.Create(new JwtOptions
            {
                Issuer = "EventEase",
                Audience = "EventEaseClients",
                // Test-only signing material; 32+ bytes so it satisfies HMAC-SHA256.
                Key = "test-signing-key-that-is-long-enough-1234567890",
                AccessTokenMinutes = 60,
                RefreshTokenDays = 7
            });

            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

            _auth = new AuthService(_db, new TokenService(jwt), new StubHttpClientFactory(), config);
        }

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
        }

        private async Task<AuthTokens> RegisterAsync(string email = "user@example.test")
        {
            return await _auth.RegisterWithPasswordAsync(new RegisterWithPasswordDto(
                name: "Test User",
                email: email,
                password: "Str0ng!Passw0rd",
                phone: "9999999999"));
        }

        [Fact]
        public async Task RegistrationReturnsAUsableRefreshToken()
        {
            var tokens = await RegisterAsync();

            Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
            Assert.True(tokens.RefreshExpires > DateTime.UtcNow);
        }

        [Fact]
        public async Task RefreshTokensAreStoredHashed()
        {
            var tokens = await RegisterAsync();

            var stored = await _db.RefreshTokens.SingleAsync();
            Assert.NotEqual(tokens.RefreshToken, stored.Token);
        }

        [Fact]
        public async Task RefreshIssuesANewPairAndSpendsTheOldToken()
        {
            var original = await RegisterAsync();

            var refreshed = await _auth.RefreshAsync(original.RefreshToken);

            Assert.NotNull(refreshed);
            Assert.NotEqual(original.RefreshToken, refreshed!.RefreshToken);

            // The original is now spent.
            Assert.Null(await _auth.RefreshAsync(original.RefreshToken));
        }

        [Fact]
        public async Task ReusingASpentTokenRevokesTheWholeFamily()
        {
            var original = await RegisterAsync();
            var refreshed = await _auth.RefreshAsync(original.RefreshToken);
            Assert.NotNull(refreshed);

            // Replaying the spent token signals theft...
            Assert.Null(await _auth.RefreshAsync(original.RefreshToken));

            // ...so the token that replaced it is revoked too.
            Assert.Null(await _auth.RefreshAsync(refreshed!.RefreshToken));
            Assert.All(await _db.RefreshTokens.ToListAsync(), t => Assert.True(t.Revoked));
        }

        [Fact]
        public async Task UnknownTokenIsRejected()
        {
            await RegisterAsync();
            Assert.Null(await _auth.RefreshAsync("not-a-real-token"));
        }

        [Fact]
        public async Task ExpiredTokenIsRejected()
        {
            var tokens = await RegisterAsync();

            var stored = await _db.RefreshTokens.SingleAsync();
            stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await _db.SaveChangesAsync();

            Assert.Null(await _auth.RefreshAsync(tokens.RefreshToken));
        }

        [Fact]
        public async Task LogoutRevokesTheToken()
        {
            var tokens = await RegisterAsync();

            await _auth.LogoutAsync(tokens.RefreshToken);

            Assert.Null(await _auth.RefreshAsync(tokens.RefreshToken));
        }

        [Fact]
        public async Task SuspendedAccountsCannotRefresh()
        {
            var tokens = await RegisterAsync();

            var user = await _db.Users.SingleAsync();
            user.AccountStatus = "suspended";
            await _db.SaveChangesAsync();

            Assert.Null(await _auth.RefreshAsync(tokens.RefreshToken));
        }

        [Fact]
        public async Task LoginRejectsSuspendedAccounts()
        {
            await RegisterAsync("suspended@example.test");

            var user = await _db.Users.SingleAsync(u => u.Email == "suspended@example.test");
            user.AccountStatus = "banned";
            await _db.SaveChangesAsync();

            var result = await _auth.LoginAsync(new LoginDto("suspended@example.test", "Str0ng!Passw0rd", null));
            Assert.Null(result);
        }

        /// <summary>Social sign-in is not exercised here, so the factory is never called.</summary>
        private sealed class StubHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new();
        }
    }
}
