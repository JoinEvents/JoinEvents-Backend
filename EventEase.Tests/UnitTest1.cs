using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using static EventEase.Application.Auth.Dtos;

namespace EventEase.Tests
{
    public class AuthIntegrationTests : IClassFixture<WebApplicationFactory<global::Program>>
    {
        private readonly HttpClient _client;
        public AuthIntegrationTests(WebApplicationFactory<global::Program> factory)
        {
            _client = factory.CreateClient();
        }

        [Theory]
        [InlineData("customer@gmail.com", "Customer")]
        [InlineData("vendor@gmail.com", "Vendor")]
        [InlineData("support@gmail.com", "Support")]
        [InlineData("admin@gmail.com", "Admin")]
        public async Task Login_Seeded_Users_Should_Succeed(string email, string expectedRole)
        {
            var loginRequest = new { email = email, password = "test" };
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
            response.EnsureSuccessStatusCode();

            var loginResult = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var token = loginResult.GetProperty("token").GetString();
            Assert.NotNull(token);
            Assert.NotEmpty(token);
        }
    }
}