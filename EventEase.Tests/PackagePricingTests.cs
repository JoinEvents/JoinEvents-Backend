using Microsoft.AspNetCore.Mvc.Testing;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using EventEase.Infrastructure.Data;
using EventEase.Core.Entities;
using Xunit;

namespace EventEase.Tests
{
    public class PackagePricingTests : IClassFixture<WebApplicationFactory<global::Program>>
    {
        private readonly WebApplicationFactory<global::Program> _factory;
        private readonly HttpClient _client;

        public PackagePricingTests(WebApplicationFactory<global::Program> factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task CreatePackage_Should_CalculateBasePrice_Dynamically_IncludingGST()
        {
            // 1. Register a Vendor
            var email = $"vendor_{Guid.NewGuid()}@test.com";
            var registerPayload = new
            {
                name = "Vendor Test",
                email = email,
                password = "Password123!",
                phone = "9999999999",
                role = "Vendor"
            };

            var regRes = await _client.PostAsJsonAsync("/api/v1/auth/register", registerPayload);
            regRes.EnsureSuccessStatusCode();
            var regData = await regRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var token = regData.GetProperty("token").GetString();
            Assert.NotNull(token);

            // 2. Validate the Vendor profile in the DbContext directly
            Guid userId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
                userId = user!.Id;

                var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
                if (vendor == null)
                {
                    vendor = new Vendor
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId
                    };
                    db.Vendors.Add(vendor);
                }
                vendor.BusinessName = "Perfect Weddings Co";
                vendor.Description = "Premium wedding planning and decors";
                vendor.Location = "Bangalore";
                vendor.IsValidated = true;
                await db.SaveChangesAsync();
            }

            // 3. Prepare Package Payload
            // BasePrice calculation: (150 guests * 600 catering + 40000 venue + 10000 decor) * 1.18 = 165200
            var descriptionWithInclusions = "A dream wedding package.\n\n---INCLUSION_DETAILS---\n{\"Catering\":{\"minPrice\":600,\"maxPrice\":600},\"Venue\":{\"minPrice\":40000,\"maxPrice\":40000},\"Decor\":{\"minPrice\":10000,\"maxPrice\":10000}}";
            
            var packagePayload = new
            {
                category = "wedding",
                name = "Elite Dream Wedding",
                description = descriptionWithInclusions,
                theme = "Premium",
                experience = 5,
                includes = new List<string> { "Catering", "Venue", "Decor" },
                capacity = new { maxGuests = 150 },
                pricing = new { unit = "per event" },
                images = new List<string> { "http://test.com/image1.jpg" }
            };

            // 4. Send Create Package request
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/vendor/packages");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(packagePayload);

            var createRes = await _client.SendAsync(request);
            if (!createRes.IsSuccessStatusCode)
            {
                var responseContent = await createRes.Content.ReadAsStringAsync();
                throw new Exception($"Create package failed with status {createRes.StatusCode}: {responseContent}");
            }

            var createData = await createRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var packageIdStr = createData.GetProperty("id").GetString();
            Assert.NotNull(packageIdStr);
            Assert.StartsWith("pkg_", packageIdStr);

            // 5. Verify the package base price in database
            var packageGuid = Guid.Parse(packageIdStr.Substring(4));
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
                var package = await db.Packages.FindAsync(packageGuid);
                Assert.NotNull(package);
                Assert.NotNull(package.Pricing);
                
                // Expected: (150 * 600 + 40000 + 10000) * 1.18 = 165200
                Assert.Equal(165200m, package.Pricing.BasePrice);
            }
        }

        [Fact]
        public async Task CuisineAndCuisineType_Should_SaveAndBind_Successfully()
        {
            // 1. Register a Vendor
            var email = $"vendor_{Guid.NewGuid()}@test.com";
            var registerPayload = new
            {
                name = "Cuisine Vendor",
                email = email,
                password = "Password123!",
                phone = "9999999999",
                role = "Vendor"
            };

            var regRes = await _client.PostAsJsonAsync("/api/v1/auth/register", registerPayload);
            regRes.EnsureSuccessStatusCode();
            var regData = await regRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var token = regData.GetProperty("token").GetString();
            Assert.NotNull(token);

            // 2. Validate the Vendor profile in the DbContext directly
            Guid userId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EventEaseDbContext>();
                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
                userId = user!.Id;

                var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
                if (vendor == null)
                {
                    vendor = new Vendor
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId
                    };
                    db.Vendors.Add(vendor);
                }
                vendor.BusinessName = "Cuisine Catering Co";
                vendor.Description = "Premium catering services";
                vendor.Location = "Bangalore";
                vendor.IsValidated = true;
                await db.SaveChangesAsync();
            }

            // 3. Prepare Package Payload with Cuisine
            var packagePayload = new
            {
                category = "catering",
                name = "Elite Cuisine Package",
                description = "Gourmet catering services",
                theme = "Premium",
                experience = 5,
                pricing = new { 
                    unit = "per plate",
                    cuisine = "South Indian",
                    cuisineType = "veg",
                    vegPrice = 350m
                },
                capacity = new { maxGuests = 100 },
                images = new List<string> { "http://test.com/catering1.jpg" }
            };

            // 4. Send Create Package request
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/vendor/packages");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(packagePayload);

            var createRes = await _client.SendAsync(request);
            if (!createRes.IsSuccessStatusCode)
            {
                var responseContent = await createRes.Content.ReadAsStringAsync();
                throw new Exception($"Create package failed: {responseContent}");
            }

            var createData = await createRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var packageIdStr = createData.GetProperty("id").GetString();
            Assert.NotNull(packageIdStr);

            // 5. Retrieve package and verify cuisine and cuisineType are mapped
            var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/vendor/packages/{packageIdStr}");
            getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var getRes = await _client.SendAsync(getRequest);
            getRes.EnsureSuccessStatusCode();

            var getData = await getRes.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var pricingProp = getData.TryGetProperty("pricing", out var p1) ? p1 : getData.GetProperty("Pricing");
            var cuisineProp = pricingProp.TryGetProperty("cuisine", out var c1) ? c1 : pricingProp.GetProperty("Cuisine");
            var cuisineTypeProp = pricingProp.TryGetProperty("cuisineType", out var ct1) ? ct1 : pricingProp.GetProperty("CuisineType");

            Assert.Equal("South Indian", cuisineProp.GetString());
            Assert.Equal("veg", cuisineTypeProp.GetString());

            // 6. Update the package - change cuisine and cuisine type
            var updatePayload = new
            {
                category = "catering",
                name = "Elite Cuisine Package Updated",
                description = "Gourmet catering services updated",
                theme = "Premium",
                experience = 5,
                pricing = new { 
                    unit = "per plate",
                    cuisine = "Chinese",
                    cuisineType = "mixed",
                    vegPrice = 350m,
                    nonVegPrice = 450m
                },
                capacity = new { maxGuests = 120 },
                images = new List<string> { "http://test.com/catering1.jpg" }
            };

            var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/vendor/packages/{packageIdStr}");
            updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            updateRequest.Content = JsonContent.Create(updatePayload);

            var updateRes = await _client.SendAsync(updateRequest);
            updateRes.EnsureSuccessStatusCode();

            // 7. Verify update in database & via GET response
            var getRequest2 = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/vendor/packages/{packageIdStr}");
            getRequest2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var getRes2 = await _client.SendAsync(getRequest2);
            getRes2.EnsureSuccessStatusCode();

            var getData2 = await getRes2.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var pricingProp2 = getData2.TryGetProperty("pricing", out var p2) ? p2 : getData2.GetProperty("Pricing");
            var cuisineProp2 = pricingProp2.TryGetProperty("cuisine", out var c2) ? c2 : pricingProp2.GetProperty("Cuisine");
            var cuisineTypeProp2 = pricingProp2.TryGetProperty("cuisineType", out var ct2) ? ct2 : pricingProp2.GetProperty("CuisineType");

            Assert.Equal("Chinese", cuisineProp2.GetString());
            Assert.Equal("mixed", cuisineTypeProp2.GetString());
        }
    }
}
