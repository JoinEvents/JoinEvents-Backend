using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventEase.Tests
{
    /// <summary>
    /// Boots the real API for integration tests against an in-memory database.
    ///
    /// The plain <see cref="WebApplicationFactory{T}"/> cannot start this app any more:
    /// startup deliberately fails when the JWT key or connection string is missing, rather
    /// than falling back to an insecure default.
    /// </summary>
    public class TestWebApplicationFactory : WebApplicationFactory<global::Program>
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();

        static TestWebApplicationFactory()
        {
            // These have to be environment variables, not ConfigureAppConfiguration values.
            // Program.cs is a minimal-hosting app that reads builder.Configuration in its
            // top-level statements — before WebApplicationFactory's configuration callbacks
            // are applied — so anything supplied through the builder arrives too late and
            // startup still throws. WebApplication.CreateBuilder reads environment variables
            // as part of its default configuration, and a static constructor runs before the
            // fixture is used, so these are in place by the time the host is built.
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            // Test-only signing material, long enough to satisfy the HMAC-SHA256 length check.
            Environment.SetEnvironmentVariable("Jwt__Key", "integration-test-signing-key-at-least-32-bytes-long");
            Environment.SetEnvironmentVariable("Jwt__Issuer", "EventEase");
            Environment.SetEnvironmentVariable("Jwt__Audience", "EventEaseClients");

            // Never used to connect — the provider is replaced below — but startup validates
            // that a connection string is present.
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__DefaultConnection",
                "Server=(localdb)\\test;Database=EventEaseTests;Trusted_Connection=True;");

            Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
            Environment.SetEnvironmentVariable("Database__SeedDemoData", "false");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                // Drop the SQL Server registration and everything hanging off it, then point
                // the context at an in-memory store so the tests need no database.
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
            });
        }
    }
}
