using EventEase.Api.Realtime;
using EventEase.Application.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EventEase.Api.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IMessengerService _messengerService;
        private readonly IRealtimeNotifier _notifier;

        public ChatHub(IMessengerService messengerService, IRealtimeNotifier notifier)
        {
            _messengerService = messengerService;
            _notifier = notifier;
        }

        /// <summary>
        /// Every connection joins its user's group, so messages and notifications reach the user
        /// on every open app and tab without subscribing to each conversation.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = GetUserId();
            if (userId != Guid.Empty)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeNotifier.UserGroup(userId));
            }
            await base.OnConnectedAsync();
        }

        public async Task SendMessage(Guid threadId, string message)
        {
            var senderId = GetUserId();
            if (senderId == Guid.Empty)
            {
                await Clients.Caller.SendAsync("ErrorMessage", "Your session is no longer valid.");
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                await Clients.Caller.SendAsync("ErrorMessage", "Message cannot be empty.");
                return;
            }

            // SendMessageAsync re-checks participation; returning null covers both "not a member"
            // and "thread closed", which are deliberately indistinguishable to the caller.
            var result = await _messengerService.SendMessageAsync(threadId, senderId, new Dtos.SendMessageRequest(message));

            if (result != null)
            {
                await _notifier.MessageAsync(await _messengerService.ParticipantUserIdsAsync(threadId), result);
            }
            else
            {
                await Clients.Caller.SendAsync("ErrorMessage", "Chat session is closed or thread not found.");
            }
        }

        /// <summary>
        /// Subscribes the connection to a thread's broadcasts.
        ///
        /// [SECURITY] Membership is verified here. This previously added any caller to any group
        /// by id, so an authenticated user could listen in on other people's conversations.
        /// </summary>
        public async Task JoinThread(Guid threadId)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty || !await _messengerService.IsParticipantAsync(threadId, userId))
            {
                throw new HubException("You do not have access to this conversation.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, threadId.ToString());
        }

        public async Task LeaveThread(Guid threadId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, threadId.ToString());
        }

        /// <summary>
        /// Reads the caller's id from the token. Returns Guid.Empty rather than throwing: the
        /// previous Guid.Parse(...) faulted the connection on any token without a sub claim.
        /// </summary>
        private Guid GetUserId()
        {
            var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);

            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }
}
