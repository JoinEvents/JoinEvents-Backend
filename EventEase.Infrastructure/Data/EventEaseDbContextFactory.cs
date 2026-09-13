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

            if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("${"))
            {
                connectionString = Environment.GetEnvironmentVariable("EVENT_EASE_DB_CONNECTION")
                                  ?? "Server=CHIRU\\SQLEXPRESS;Database=EventEaseDb;User Id=sa;Password=Chiru5512#;TrustServerCertificate=True;";
            }

            var optionsBuilder = new DbContextOptionsBuilder<EventEaseDbContext>();
            optionsBuilder.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(120));

            return new EventEaseDbContext(optionsBuilder.Options);
        }
    }
}