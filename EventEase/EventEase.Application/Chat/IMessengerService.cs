using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static EventEase.Application.Chat.Dtos;

namespace EventEase.Application.Chat
{
    public interface IMessengerService
    {
        Task<List<ThreadPreviewResponse>> GetThreadsAsync(Guid userId);
        Task<MessageResponse?> SendMessageAsync(Guid threadId, Guid senderId, SendMessageRequest dto);
        Task<bool> IsChatSessionAliveAsync(Guid threadId);
        Task<List<MessageResponse>> GetMessagesAsync(Guid threadId);
        Task<Guid> RequestChatAsync(Guid customerId, Guid vendorId, Guid? rfpId, string? initialMessage);
        Task<bool> AcceptChatAsync(Guid threadId);
        Task<bool> RejectChatAsync(Guid threadId);
    }
}
