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
        public ChatHub(IMessengerService messengerService) => _messengerService = messengerService;

        public async Task SendMessage(Guid threadId, string message)
        {
            var senderId = Guid.Parse(Context.User.FindFirstValue(JwtRegisteredClaimNames.Sub));
            
            var result = await _messengerService.SendMessageAsync(threadId, senderId, new Dtos.SendMessageRequest(message));
            
            if (result != null)
            {
                // In a real app, you might want to send to a group named by threadId
                // For simplicity, we are sending to all but you should filter by recipients
                await Clients.Group(threadId.ToString()).SendAsync("ReceiveMessage", result);
            }
            else
            {
                await Clients.Caller.SendAsync("ErrorMessage", "Chat session is closed or thread not found.");
            }
        }

        public async Task JoinThread(Guid threadId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, threadId.ToString());
        }

        public async Task LeaveThread(Guid threadId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, threadId.ToString());
        }
    }
}
