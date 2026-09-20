using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventEase.Tests
{
    /// <summary>
    /// Boots the real API for integration tests against an in-memory database.
    ///
    /// The plain <see cref="WebApplicationFactory{T}"/> cannot start this app any more: startup
    /// deliberately fails when the JWT key or connection string is missing, rather than falling
    /// back to an insecure default. This supplies test values for both and swaps SQL Server for
    /// the in-memory provider, so tests need no database.
    /// </summary>
    public class TestWebApplicationFactory : WebApplicationFactory<global::Program>
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development skips the production-only guards (CORS origins must be configured,
            // and the simulator payment gateway is refused) that are irrelevant here.
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Never used to connect — the provider is replaced below — but startup
                    // validates that one is present.
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=(localdb)\\test;Database=EventEaseTests;Trusted_Connection=True;",

                    // Test-only signing material, long enough to satisfy the HMAC-SHA256 check.
                    ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long",
                    ["Jwt:Issuer"] = "EventEase",
                    ["Jwt:Audience"] = "EventEaseClients",

                    ["Database:MigrateOnStartup"] = "false",
                    ["Database:SeedDemoData"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                // Drop the SQL Server registration and everything that hangs off it.
                var toRemove = services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<EventEaseDbContext>)
                             || d.ServiceType == typeof(DbContextOptions)
                             || d.ServiceType == typeof(EventEaseDbContext))
                    .ToList();

                foreach (var descriptor in toRemove)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<EventEaseDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName));

                // Create the schema once so the first request does not race on it.
                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<EventEaseDbContext>()
                     .Database.EnsureCreated();
            });
        }
    }
}
