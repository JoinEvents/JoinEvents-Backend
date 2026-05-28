using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using EventEase.Infrastructure.Data;

namespace EventEase.Infrastructure.Data
{
    public class EventEaseDbContextFactory : IDesignTimeDbContextFactory<EventEaseDbContext>
    {
        public EventEaseDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<EventEaseDbContext>();
            // Use your actual connection string here
            optionsBuilder.UseSqlServer("Server=CHIRU\\SQLEXPRESS;Database=EventEaseDb;User Id=sa;Password=Chiru5512#;TrustServerCertificate=True;", sql => sql.UseCompatibilityLevel(120));
            return new EventEaseDbContext(optionsBuilder.Options);
        }
    }
}