using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EventEase.Infrastructure.Data
{
    public class EventEaseDbContextFactory : IDesignTimeDbContextFactory<EventEaseDbContext>
    {
        public EventEaseDbContext CreateDbContext(string[] args)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

            var basePath = Directory.GetCurrentDirectory();
            var apiPath = Path.Combine(basePath, "EventEase");
            if (!File.Exists(Path.Combine(basePath, "appsettings.json")) && File.Exists(Path.Combine(apiPath, "appsettings.json")))
            {
                basePath = apiPath;
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // [SECURITY] No hardcoded fallback. This previously fell back to a connection
            // string containing a real password, which was committed to the repository.
            if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("${"))
            {
                throw new InvalidOperationException(
                    "No database connection string is configured for design-time use. Set the " +
                    "ConnectionStrings__DefaultConnection environment variable before running " +
                    "'dotnet ef', for example:\n" +
                    "  export ConnectionStrings__DefaultConnection='Server=...;Database=...;...'");
            }

            var optionsBuilder = new DbContextOptionsBuilder<EventEaseDbContext>();
            optionsBuilder.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(120));

            return new EventEaseDbContext(optionsBuilder.Options);
        }
    }
}