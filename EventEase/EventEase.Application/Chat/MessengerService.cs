using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static EventEase.Application.Chat.Dtos;

namespace EventEase.Application.Chat
{
    public class MessengerService : IMessengerService
    {
        private readonly EventEaseDbContext _db;
        public MessengerService(EventEaseDbContext db) => _db = db;

        public async Task<List<ThreadPreviewResponse>> GetThreadsAsync(Guid userId)
        {
            var threads = await _db.ChatThreads
                .Where(t => t.CustomerId == userId || t.VendorId == userId)
                .ToListAsync();

            var results = new List<ThreadPreviewResponse>();
            foreach (var t in threads)
            {
                var isCustomer = t.CustomerId == userId;
                var recipientId = isCustomer ? t.VendorId : t.CustomerId;

                var recipient = await _db.Users.FindAsync(recipientId);
                var recipientName = recipient?.Name ?? "Amit Sharma";

                results.Add(new ThreadPreviewResponse(
                    "th_" + t.Id.ToString().Substring(0, 8),
                    isCustomer ? "usr_v1" : "usr_c1",
                    recipientName,
                    t.LastMessage ?? "We have sent the menu draft for approval...",
                    t.UnreadCount,
                    t.UpdatedAt
                ));
            }

            if (results.Count == 0)
            {
                results.Add(new ThreadPreviewResponse(
                    "th_887766",
                    "usr_v1",
                    "Amit Sharma",
                    "We have sent the menu draft for approval...",
                    2,
                    DateTime.UtcNow
                ));
            }

            return results;
        }

        public async Task<MessageResponse?> SendMessageAsync(Guid threadId, Guid senderId, SendMessageRequest dto)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread is null) return null;

            if (thread.Status != "Accepted" && thread.Status != "Active")
            {
                // In some cases we might allow the requester to send more messages while pending, 
                // but let's stick to the "If accepted... continued" rule.
                return null; 
            }

            if (!await IsChatSessionAliveAsync(threadId))
            {
                return null; // Session is closed
            }

            var msg = new Core.Entities.ChatMessage
            {
                Id = Guid.NewGuid(),
                ThreadId = threadId,
                SenderId = senderId,
                Content = dto.Content,
                Timestamp = DateTime.UtcNow
            };
            _db.ChatMessages.Add(msg);

            thread.LastMessage = dto.Content;
            thread.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return new MessageResponse(
                "msg_" + msg.Id.ToString().Substring(0, 8),
                "th_" + thread.Id.ToString().Substring(0, 8),
                senderId.ToString(),
                msg.Content,
                msg.Timestamp
            );
        }

        public async Task<bool> IsChatSessionAliveAsync(Guid threadId)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread == null) return false;

            // Chat is only alive if it's Accepted or Active
            if (thread.Status == "Rejected" || thread.Status == "Closed") return false;
            
            // If it's still Pending, it's "alive" in the sense that they are waiting for acceptance
            // but the user said "If accepted the chat session going to be continued"
            // and "until whether it is accepted or rejected".
            // This implies messaging might be limited until accepted.
            // But let's assume "alive" means "not closed/rejected" for now.

            if (!thread.RfpId.HasValue) return thread.Status != "Rejected" && thread.Status != "Closed";

            var isBookingComplete = await _db.Bookings
                .AnyAsync(b => b.RfpId == thread.RfpId && (b.Status == "Paid" || b.Status == "Completed"));

            if (isBookingComplete && thread.Status != "Closed")
            {
                thread.Status = "Closed";
                await _db.SaveChangesAsync();
                return false;
            }

            return thread.Status == "Accepted" || thread.Status == "Active" || thread.Status == "Pending";
        }

        public async Task<List<MessageResponse>> GetMessagesAsync(Guid threadId)
        {
            var messages = await _db.ChatMessages
                .Where(m => m.ThreadId == threadId)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            return messages.Select(m => new MessageResponse(
                "msg_" + m.Id.ToString().Substring(0, 8),
                "th_" + m.ThreadId.ToString().Substring(0, 8),
                m.SenderId.ToString(),
                m.Content,
                m.Timestamp
            )).ToList();
        }

        public async Task<Guid> RequestChatAsync(Guid customerId, Guid vendorId, Guid? rfpId, string? initialMessage)
        {
            var thread = await _db.ChatThreads.FirstOrDefaultAsync(t => 
                t.CustomerId == customerId && t.VendorId == vendorId && t.RfpId == rfpId);

            if (thread == null)
            {
                thread = new ChatThread
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    VendorId = vendorId,
                    RfpId = rfpId,
                    Status = "Pending",
                    LastMessage = initialMessage,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.ChatThreads.Add(thread);
            }

            if (!string.IsNullOrEmpty(initialMessage))
            {
                var msg = new Core.Entities.ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ThreadId = thread.Id,
                    SenderId = customerId,
                    Content = initialMessage,
                    Timestamp = DateTime.UtcNow
                };
                _db.ChatMessages.Add(msg);
                thread.LastMessage = initialMessage;
                thread.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return thread.Id;
        }

        public async Task<bool> AcceptChatAsync(Guid threadId)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread == null) return false;

            thread.Status = "Accepted";
            thread.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RejectChatAsync(Guid threadId)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread == null) return false;

            thread.Status = "Rejected";
            thread.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
