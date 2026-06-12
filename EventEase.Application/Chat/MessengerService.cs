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

            if (threads.Count == 0) return new List<ThreadPreviewResponse>();

            var recipientIds = threads.Select(t => t.CustomerId == userId ? t.VendorId : t.CustomerId).Distinct().ToList();
            var recipients = await _db.Users.Where(u => recipientIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u);

            var vendorUserIds = threads.Select(t => t.VendorId).Distinct().ToList();
            var vendorsList = await _db.Vendors.Where(v => vendorUserIds.Contains(v.UserId)).ToListAsync();
            var vendorsDict = vendorsList.GroupBy(v => v.UserId).ToDictionary(g => g.Key, g => g.First());

            var rfpIds = threads.Where(t => t.RfpId.HasValue).Select(t => t.RfpId!.Value).Distinct().ToList();
            var rfps = await _db.Rfps.Where(r => rfpIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r);

            var customerIds = threads.Select(t => t.CustomerId).ToList();
            var vendorIds = threads.Select(t => t.VendorId).ToList();
            var bookings = await _db.Bookings
                .Where(b => customerIds.Contains(b.UserId) && vendorIds.Contains(b.VendorId))
                .OrderByDescending(b => b.EventDate)
                .ToListAsync();

            var threadIds = threads.Select(t => t.Id).ToList();
            var lastMessages = await _db.ChatMessages
                .Where(m => threadIds.Contains(m.ThreadId))
                .GroupBy(m => m.ThreadId)
                .Select(g => new { ThreadId = g.Key, Message = g.OrderByDescending(m => m.Timestamp).FirstOrDefault() })
                .ToListAsync();

            var lastMsgDict = lastMessages
                .Where(x => x.Message != null)
                .ToDictionary(x => x.ThreadId, x => x.Message);

            return threads.Select(t =>
            {
                var isCustomer = t.CustomerId == userId;
                var recipientId = isCustomer ? t.VendorId : t.CustomerId;
                var recipient = recipients.GetValueOrDefault(recipientId);
                var lastMsg = lastMsgDict.GetValueOrDefault(t.Id);
                var displayUnreadCount = lastMsg != null && lastMsg.SenderId != userId ? t.UnreadCount : 0;

                string recipientName = "Unknown";
                string? mappedVendorIdStr = null;

                if (isCustomer)
                {
                    var vendorProfile = vendorsDict.GetValueOrDefault(t.VendorId);
                    if (vendorProfile != null)
                    {
                        recipientName = vendorProfile.BusinessName;
                        mappedVendorIdStr = vendorProfile.Id.ToString();
                    }
                    else
                    {
                        recipientName = recipient?.Name ?? "Unknown";
                    }
                }
                else
                {
                    recipientName = recipient?.Name ?? "Unknown";
                }

                string? eventTitle = null;
                if (t.RfpId.HasValue && rfps.TryGetValue(t.RfpId.Value, out var rfp))
                {
                    eventTitle = rfp.Title;
                }

                if (string.IsNullOrEmpty(eventTitle))
                {
                    var booking = t.RfpId.HasValue
                        ? bookings.FirstOrDefault(b => b.Id == t.RfpId.Value)
                        : bookings.FirstOrDefault(b => b.UserId == t.CustomerId && b.VendorId == t.VendorId);
                    if (booking != null)
                    {
                        eventTitle = !string.IsNullOrEmpty(booking.EventName)
                            ? booking.EventName
                            : (!string.IsNullOrEmpty(booking.PackageName) ? booking.PackageName : "Event Booking");
                    }
                }

                return new ThreadPreviewResponse(
                    t.Id.ToString(),
                    recipientId.ToString(),
                    recipientName,
                    recipient?.Avatar,
                    t.LastMessage ?? "",
                    displayUnreadCount,
                    DateTime.SpecifyKind(t.UpdatedAt, DateTimeKind.Utc),
                    t.Status,
                    eventTitle,
                    mappedVendorIdStr,
                    t.RfpId?.ToString()
                );
            }).ToList();
        }

        public async Task<MessageResponse?> SendMessageAsync(Guid threadId, Guid senderId, SendMessageRequest dto)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread is null) return null;

            if (thread.Status == "Rejected" || thread.Status == "Closed")
            {
                return null;
            }

            if (thread.RfpId.HasValue)
            {
                var isBookingComplete = await _db.Bookings
                    .AnyAsync(b => b.RfpId == thread.RfpId && (b.Status == "Paid" || b.Status == "Completed"));
                if (isBookingComplete)
                {
                    thread.Status = "Closed";
                    thread.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    return null;
                }
            }

            var now = DateTime.UtcNow;
            var msg = new Core.Entities.ChatMessage
            {
                Id = Guid.NewGuid(),
                ThreadId = threadId,
                SenderId = senderId,
                Content = dto.Content,
                Timestamp = now
            };
            _db.ChatMessages.Add(msg);

            thread.LastMessage = dto.Content;
            thread.UpdatedAt = now;
            thread.UnreadCount += 1;

            var sender = await _db.Users.FindAsync(senderId);

            await _db.SaveChangesAsync();

            return new MessageResponse(
                msg.Id.ToString(),
                thread.Id.ToString(),
                senderId.ToString(),
                sender?.Name ?? "Unknown",
                sender?.Avatar,
                msg.Content,
                now
            );
        }

        public async Task<bool> IsChatSessionAliveAsync(Guid threadId)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread == null) return false;

            if (thread.Status == "Rejected" || thread.Status == "Closed") return false;

            if (!thread.RfpId.HasValue) return thread.Status != "Rejected" && thread.Status != "Closed";

            var isBookingComplete = await _db.Bookings
                .AnyAsync(b => b.RfpId == thread.RfpId && (b.Status == "Paid" || b.Status == "Completed"));

            return !isBookingComplete && (thread.Status == "Accepted" || thread.Status == "Active" || thread.Status == "Pending");
        }

        public async Task<List<MessageResponse>> GetMessagesAsync(Guid threadId)
        {
            var messages = await _db.ChatMessages
                .Where(m => m.ThreadId == threadId)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            var senderIds = messages.Select(m => m.SenderId).Distinct().ToList();
            var senders = await _db.Users.Where(u => senderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u);

            return messages.Select(m =>
            {
                var sender = senders.GetValueOrDefault(m.SenderId);
                return new MessageResponse(
                    m.Id.ToString(),
                    m.ThreadId.ToString(),
                    m.SenderId.ToString(),
                    sender?.Name ?? "Unknown",
                    sender?.Avatar,
                    m.Content,
                    DateTime.SpecifyKind(m.Timestamp, DateTimeKind.Utc)
                );
            }).ToList();
        }

        public async Task<Guid> RequestChatAsync(Guid customerId, Guid vendorId, Guid? rfpId, string? initialMessage)
        {
            var vendor = await _db.Vendors.FindAsync(vendorId);
            var vendorUserId = vendor != null ? vendor.UserId : vendorId;

            var now = DateTime.UtcNow;
            var thread = await _db.ChatThreads.FirstOrDefaultAsync(t => 
                t.CustomerId == customerId && t.VendorId == vendorUserId && t.RfpId == rfpId);

            if (thread == null)
            {
                thread = new ChatThread
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    VendorId = vendorUserId,
                    RfpId = rfpId,
                    Status = "Pending",
                    LastMessage = initialMessage,
                    UpdatedAt = now,
                    UnreadCount = string.IsNullOrEmpty(initialMessage) ? 0 : 1
                };
                _db.ChatThreads.Add(thread);
            }
            else
            {
                if (!string.IsNullOrEmpty(initialMessage))
                {
                    thread.UnreadCount += 1;
                    thread.UpdatedAt = now;
                }
            }

            if (!string.IsNullOrEmpty(initialMessage))
            {
                var msg = new Core.Entities.ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ThreadId = thread.Id,
                    SenderId = customerId,
                    Content = initialMessage,
                    Timestamp = now
                };
                _db.ChatMessages.Add(msg);
                thread.LastMessage = initialMessage;
                thread.UpdatedAt = now;
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

        public async Task<bool> MarkAsReadAsync(Guid threadId, Guid userId)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread == null) return false;

            thread.UnreadCount = 0;
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
