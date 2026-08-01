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

        [Fact]
        public async Task Register_And_Login_Should_Return_Tokens()
        {
            var uniqueEmail = $"test_{Guid.NewGuid()}@test.com";
            var reg = new { 
                name = "Test User", 
                email = uniqueEmail, 
                password = "Password123!", 
                phone = "7777777777", 
                role = "Customer" 
            };
            
            // Register
            var res = await _client.PostAsJsonAsync("/api/v1/auth/register", reg);
            res.EnsureSuccessStatusCode();
            var regResult = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var token = regResult.GetProperty("token").GetString();
            Assert.NotNull(token);

            // Login
            var login = new { 
                email = uniqueEmail, 
                password = "Password123!" 
            };
            var res2 = await _client.PostAsJsonAsync("/api/v1/auth/login", login);
            res2.EnsureSuccessStatusCode();
            var loginResult = await res2.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var loginToken = loginResult.GetProperty("token").GetString();
            Assert.NotNull(loginToken);
        }
    }
}