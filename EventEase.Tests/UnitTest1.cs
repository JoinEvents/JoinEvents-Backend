using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using System.Net.Http.Json;
using static EventEase.Application.Auth.Dtos;

namespace EventEase.Tests
{
    public class AuthIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;
        public AuthIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Register_And_Verify_Should_Return_Tokens()
        {
            var reg = new { name = "Test", phone = "7777777777", email = "t@t.com" };
            var res = await _client.PostAsJsonAsync("/auth/register", reg);
            res.EnsureSuccessStatusCode();

            // In dev, OTP is returned in response or logs
            var verify = new { phone = "7777777777", otp = "123456" };
            var res2 = await _client.PostAsJsonAsync("/auth/verify", verify);
            res2.EnsureSuccessStatusCode();
            var tokens = await res2.Content.ReadFromJsonAsync<AuthTokens>();
            Assert.NotNull(tokens?.AccessToken);
        }
    }

}