using System;

namespace EventEase.Application.Chat
{
    public class Dtos
    {
        public record ThreadPreviewResponse(string ThreadId, string RecipientId, string RecipientName, string LastMessage, int UnreadCount, DateTime UpdatedAt);
        public record SendMessageRequest(string Content);
        public record MessageResponse(string MessageId, string ThreadId, string SenderId, string Content, DateTime Timestamp);
    }
}
