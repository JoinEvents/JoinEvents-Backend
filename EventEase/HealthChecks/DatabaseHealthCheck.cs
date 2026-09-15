using EventEase.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EventEase.Api
{
    /// <summary>
    /// Readiness probe: reports unhealthy when the database cannot be reached, so an instance
    /// that cannot serve traffic is taken out of the load balancer rotation.
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
                return canConnect
                    ? HealthCheckResult.Healthy("Database reachable.")
                    : HealthCheckResult.Unhealthy("Database unreachable.");
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
