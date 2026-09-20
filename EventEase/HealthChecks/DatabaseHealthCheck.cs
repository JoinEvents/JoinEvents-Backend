using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EventEase.Api
{
    /// <summary>
    /// Readiness probe: reports unhealthy when the database cannot be reached, so an instance
    /// that cannot serve traffic is taken out of the load balancer rotation.
    ///
    /// Reachable is not the same as usable. Opening a connection succeeds against a database
    /// whose migrations were never applied, so a connect-only probe reports a healthy server
    /// whose every endpoint answers 500 — which is exactly what it did while the mobile app
    /// saw /health return 200 and /api/v1/event-categories return 500. Pending migrations are
    /// therefore reported as degraded: the instance stays in rotation (pulling it out turns a
    /// broken deployment into an offline one, and a replacement instance would be no better),
    /// but the state is named in the probe response and logged.
    /// </summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly EventEaseDbContext _db;

        public DatabaseHealthCheck(EventEaseDbContext db) => _db = db;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
                if (!canConnect)
                {
                    return HealthCheckResult.Unhealthy("Database unreachable.");
                }

                // Absent the __EFMigrationsHistory table this reports every migration as
                // pending rather than throwing, which is the right answer for a database
                // that was never migrated.
                var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count > 0)
                {
                    // [SECURITY] The probe is anonymous, so the count goes to the caller and
                    // the migration names go to the log.
                    Serilog.Log.Error(
                        "Database schema is out of date: {PendingCount} migration(s) have not been applied ({PendingMigrations}). " +
                        "API endpoints will fail until they are. Run the migration release step.",
                        pending.Count,
                        string.Join(", ", pending));

                    return HealthCheckResult.Degraded(
                        $"Database reachable, but {pending.Count} migration(s) have not been applied. " +
                        "Endpoints that touch the missing tables or columns will fail until the migration step runs.");
                }

                return HealthCheckResult.Healthy("Database reachable and schema up to date.");
            }
            catch (Exception ex)
            {
                // The exception is logged, not returned: probe responses are reachable
                // without authentication.
                Serilog.Log.Error(ex, "Database health check failed");
                return HealthCheckResult.Unhealthy("Database unreachable.");
            }
        }
    }
}
