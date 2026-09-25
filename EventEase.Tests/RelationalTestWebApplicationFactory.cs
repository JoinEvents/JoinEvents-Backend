using EventEase.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EventEase.Tests
{
    /// <summary>
    /// Runs the API on a real relational database (in-memory SQLite) with a retrying execution
    /// strategy, as production runs SQL Server with EnableRetryOnFailure.
    ///
    /// The in-memory provider has no transactions, so it cannot catch code that production
    /// rejects — such as a transaction opened outside the retrying strategy, which made every
    /// booking fail with a 500.
    /// </summary>
    public class RelationalTestWebApplicationFactory : TestWebApplicationFactory
    {
        private readonly SqliteConnection _connection = new($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
        private bool _created;

        public RelationalTestWebApplicationFactory()
        {
            // The in-memory database lives as long as one connection to it stays open.
            _connection.Open();
        }

        protected override void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            options.UseSqlite(_connection.ConnectionString, sqlite =>
                sqlite.ExecutionStrategy(dependencies => new RetryingExecutionStrategy(dependencies)));
        }

        /// <summary>Creates the schema once, before a test touches the database.</summary>
        public void EnsureDatabase()
        {
            if (_created) return;
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<EventEaseDbContext>().Database.EnsureCreated();
            _created = true;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }

    /// <summary>
    /// A retrying strategy like SqlServerRetryingExecutionStrategy: it refuses to run inside a
    /// transaction that was not opened through the strategy.
    /// </summary>
    public class RetryingExecutionStrategy : ExecutionStrategy
    {
        public RetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(50))
        {
        }

        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
