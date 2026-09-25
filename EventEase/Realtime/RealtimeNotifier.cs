using EventEase.Api.Hubs;
using EventEase.Core.Entities;
using Microsoft.AspNetCore.SignalR;
using static EventEase.Application.Chat.Dtos;

namespace EventEase.Api.Realtime
{
    /// <summary>
    /// Pushes events to signed-in users over the chat hub. Every connection joins its user's
    /// group (<see cref="UserGroup"/>), so an event reaches all of that user's open apps and tabs.
    /// </summary>
    public interface IRealtimeNotifier
    {
        /// <summary>A chat message, to everyone in the conversation (the sender's other devices too).</summary>
        Task MessageAsync(IEnumerable<Guid> participantUserIds, MessageResponse message);

        /// <summary>A stored notification, to its owner.</summary>
        Task NotificationAsync(Notification notification);
    }

    public class RealtimeNotifier : IRealtimeNotifier
    {
        public const string MessageEvent = "ReceiveMessage";
        public const string NotificationEvent = "Notification";

        private readonly IHubContext<ChatHub> _hub;
        private readonly ILogger<RealtimeNotifier> _logger;

        public RealtimeNotifier(IHubContext<ChatHub> hub, ILogger<RealtimeNotifier> logger)
        {
            _hub = hub;
            _logger = logger;
        }

        public static string UserGroup(Guid userId) => $"user:{userId}";

        public async Task MessageAsync(IEnumerable<Guid> participantUserIds, MessageResponse message)
        {
            var groups = participantUserIds.Where(id => id != Guid.Empty).Distinct().Select(UserGroup).ToList();
            if (groups.Count == 0) return;
            await Safely(() => _hub.Clients.Groups(groups).SendAsync(MessageEvent, message));
        }

        public Task NotificationAsync(Notification notification) =>
            Safely(() => _hub.Clients.Group(UserGroup(notification.UserId)).SendAsync(NotificationEvent, ToPayload(notification)));

        /// <summary>The same shape GET /api/v1/notifications returns, so clients handle both alike.</summary>
        public static object ToPayload(Notification n) => new
        {
            id = "notif_" + n.Id,
            title = n.Title,
            message = n.Message,
            type = n.Type,
            isRead = n.IsRead,
            createdAt = DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc)
        };

        // A live push is a courtesy on top of what is already saved: a hub hiccup must never fail
        // the request that produced it.
        private async Task Safely(Func<Task> push)
        {
            try
            {
                await push();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Real-time push failed");
            }
        }
    }
}
