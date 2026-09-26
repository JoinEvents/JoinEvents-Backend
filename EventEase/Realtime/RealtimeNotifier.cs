using EventEase.Api.Hubs;
using EventEase.Api.Push;
using EventEase.Core.Entities;
using Microsoft.AspNetCore.SignalR;
using static EventEase.Application.Chat.Dtos;

namespace EventEase.Api.Realtime
{
    /// <summary>
    /// Pushes events to users: live over the chat hub to every open app and tab (each connection
    /// joins its user's group, <see cref="UserGroup"/>), and as a phone notification through
    /// <see cref="IPushSender"/> so it arrives even when the app is closed.
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
        private readonly IPushSender _push;
        private readonly ILogger<RealtimeNotifier> _logger;

        public RealtimeNotifier(IHubContext<ChatHub> hub, IPushSender push, ILogger<RealtimeNotifier> logger)
        {
            _hub = hub;
            _push = push;
            _logger = logger;
        }

        public static string UserGroup(Guid userId) => $"user:{userId}";

        public async Task MessageAsync(IEnumerable<Guid> participantUserIds, MessageResponse message)
        {
            var groups = participantUserIds.Where(id => id != Guid.Empty).Distinct().Select(UserGroup).ToList();
            if (groups.Count == 0) return;
            await Safely(() => _hub.Clients.Groups(groups).SendAsync(MessageEvent, message));

            // The other side's phone gets a notification; the sender's own devices don't.
            var recipients = participantUserIds
                .Where(id => id != Guid.Empty && id.ToString() != message.SenderId)
                .Distinct().ToList();
            var preview = message.Content.Length > 140 ? message.Content[..137] + "…" : message.Content;
            InBackground(recipients, new PushMessage(
                string.IsNullOrWhiteSpace(message.SenderName) ? "New message" : message.SenderName,
                preview, "message", role => PushLinks.For(role, "message", message.ThreadId)));
        }

        public async Task NotificationAsync(Notification notification)
        {
            await Safely(() => _hub.Clients.Group(UserGroup(notification.UserId)).SendAsync(NotificationEvent, ToPayload(notification)));
            InBackground(new[] { notification.UserId }, new PushMessage(
                notification.Title, notification.Message, notification.Type,
                role => PushLinks.For(role, notification.Type)));
        }

        // Phone delivery goes through Firebase and can take a moment; the request that raised the
        // event does not wait for it. SendAsync never throws.
        private void InBackground(IReadOnlyCollection<Guid> userIds, PushMessage message)
        {
            if (!_push.Enabled || userIds.Count == 0) return;
            _ = Task.Run(() => _push.SendAsync(userIds, message));
        }

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
