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
        /// <summary>The vendor on the thread accepts a customer's chat request. False otherwise.</summary>
        Task<bool> AcceptChatAsync(Guid threadId, Guid vendorUserId);
        /// <summary>The vendor on the thread declines a customer's chat request. False otherwise.</summary>
        Task<bool> RejectChatAsync(Guid threadId, Guid vendorUserId);
        Task<bool> MarkAsReadAsync(Guid threadId, Guid userId);

        /// <summary>
        /// Opens the conversation for a confirmed booking: the customer's thread with the vendor is
        /// created or reopened as Active and the vendor's note is posted in it. Returns the posted
        /// message, whose thread id is the conversation.
        /// </summary>
        Task<MessageResponse> OpenBookingThreadAsync(Guid customerId, Guid vendorUserId, Guid? rfpId, string note);

        /// <summary>The customer's and the vendor's user ids, for pushing a thread's events.</summary>
        Task<IReadOnlyList<Guid>> ParticipantUserIdsAsync(Guid threadId);

        /// <summary>
        /// True when the user is the customer or the vendor on this thread. Callers must check
        /// this before joining a thread's broadcast group or reading its history.
        /// </summary>
        Task<bool> IsParticipantAsync(Guid threadId, Guid userId);
    }
}
