using System;

namespace EventEase.Application.Chat
{
    public class Dtos
    {
        public record ThreadPreviewResponse(string ThreadId, string RecipientId, string RecipientName, string? RecipientAvatar, string LastMessage, int UnreadCount, DateTime UpdatedAt, string Status, string? EventTitle = null, string? VendorId = null);
        public record SendMessageRequest(string Content);
        public record MessageResponse(string MessageId, string ThreadId, string SenderId, string SenderName, string? SenderAvatar, string Content, DateTime Timestamp);
    }
}
