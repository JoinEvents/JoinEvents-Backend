using System.Data.Common;
using System.Runtime.CompilerServices;
using EventEase.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EventEase.Api.Realtime
{
    /// <summary>
    /// Pushes every notification the moment it is saved, whichever code path created it. The
    /// booking, payment, quote and support flows all add notifications straight to the context;
    /// catching them here makes all of them real-time without touching each one.
    /// </summary>
    public class NotificationPushInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
    {
        private readonly IServiceProvider _services;
        private readonly ConditionalWeakTable<DbContext, List<Notification>> _pending = new();

        public NotificationPushInterceptor(IServiceProvider services) => _services = services;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Capture(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Capture(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Publish(eventData.Context).GetAwaiter().GetResult();
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            await Publish(eventData.Context);
            return result;
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            if (eventData.Context is not null) _pending.Remove(eventData.Context);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            SaveChangesFailed(eventData);
            return Task.CompletedTask;
        }

        // Saves made inside a transaction publish when it commits, and are dropped if it rolls back.
        void IDbTransactionInterceptor.TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
            Publish(eventData.Context, committed: true).GetAwaiter().GetResult();

        Task IDbTransactionInterceptor.TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken) =>
            Publish(eventData.Context, committed: true);

        void IDbTransactionInterceptor.TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (eventData.Context is not null) _pending.Remove(eventData.Context);
        }

        Task IDbTransactionInterceptor.TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
        {
            if (eventData.Context is not null) _pending.Remove(eventData.Context);
            return Task.CompletedTask;
        }

        private void Capture(DbContext? context)
        {
            if (context is null) return;
            var added = context.ChangeTracker.Entries<Notification>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();
            if (added.Count == 0) return;

            var list = _pending.GetOrCreateValue(context);
            list.AddRange(added);
        }

        private async Task Publish(DbContext? context, bool committed = false)
        {
            // Inside a transaction the rows are not visible yet: they go out when it commits
            // (TransactionCommitted), or not at all if it rolls back.
            if (context is null || (!committed && context.Database.CurrentTransaction is not null)) return;
            if (!_pending.TryGetValue(context, out var list) || list.Count == 0) return;
            _pending.Remove(context);

            var notifier = _services.GetService(typeof(IRealtimeNotifier)) as IRealtimeNotifier;
            if (notifier is null) return;
            foreach (var notification in list)
            {
                await notifier.NotificationAsync(notification);
            }
        }
    }
}
