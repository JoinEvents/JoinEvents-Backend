using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using EventEase.Core.Enums;
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

            // Bookings are keyed by the Vendor profile id while threads hold the vendor's user id,
            // so both are matched (matching on the user id alone never found a booking).
            var customerIds = threads.Select(t => t.CustomerId).ToList();
            var vendorIds = threads.Select(t => t.VendorId).Concat(vendorsList.Select(v => v.Id)).Distinct().ToList();
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
                    var profileId = vendorsDict.GetValueOrDefault(t.VendorId)?.Id;
                    var booking = (t.RfpId.HasValue ? bookings.FirstOrDefault(b => b.RfpId == t.RfpId) : null)
                        ?? bookings.FirstOrDefault(b => b.UserId == t.CustomerId && (b.VendorId == t.VendorId || b.VendorId == profileId));
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

        /// <summary>
        /// True when the user is a party to the thread. ChatThread.VendorId holds the vendor's
        /// *user* id, but some legacy rows hold the Vendor profile id, so both are accepted.
        /// </summary>
        public async Task<bool> IsParticipantAsync(Guid threadId, Guid userId)
        {
            if (userId == Guid.Empty) return false;

            var thread = await _db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId);
            if (thread is null) return false;

            if (thread.CustomerId == userId || thread.VendorId == userId) return true;

            var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
            return vendor is not null && thread.VendorId == vendor.Id;
        }

        public async Task<MessageResponse?> SendMessageAsync(Guid threadId, Guid senderId, SendMessageRequest dto)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread is null) return null;

            // [SECURITY] Only the two parties to a conversation may post into it. Without this,
            // any authenticated user could write into any thread by id.
            if (!await IsParticipantAsync(threadId, senderId)) return null;

            // A booked conversation stays open. This used to close a quote-request thread as soon
            // as its booking was paid, cutting customer and vendor off right when they need to talk.
            if (thread.Status == "Rejected" || thread.Status == "Closed")
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(dto.Content)) return null;

            var now = DateTime.UtcNow;
            var msg = new Core.Entities.ChatMessage
            {
                Id = Guid.NewGuid(),
                ThreadId = threadId,
                SenderId = senderId,
                Content = dto.Content.Trim(),
                Timestamp = now
            };
            _db.ChatMessages.Add(msg);

            thread.LastMessage = msg.Content;
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

        /// <summary>A conversation is open until it is declined or closed; a booking never closes it.</summary>
        public async Task<bool> IsChatSessionAliveAsync(Guid threadId)
        {
            var thread = await _db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId);
            return thread is not null && thread.Status != "Rejected" && thread.Status != "Closed";
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
                    Status = ChatThreadStatus.Pending.ToString(),
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

        public Task<bool> AcceptChatAsync(Guid threadId, Guid vendorUserId) =>
            SetStatusAsVendorAsync(threadId, vendorUserId, ChatThreadStatus.Accepted.ToString());

        public Task<bool> RejectChatAsync(Guid threadId, Guid vendorUserId) =>
            SetStatusAsVendorAsync(threadId, vendorUserId, ChatThreadStatus.Rejected.ToString());

        /// <summary>
        /// [SECURITY] Only the vendor on the thread answers a chat request. Any signed-in user
        /// could previously accept or reject any conversation by id.
        /// </summary>
        private async Task<bool> SetStatusAsVendorAsync(Guid threadId, Guid vendorUserId, string status)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread is null || !await IsVendorOfAsync(thread, vendorUserId)) return false;

            thread.Status = status;
            thread.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        private async Task<bool> IsVendorOfAsync(ChatThread thread, Guid userId)
        {
            if (userId == Guid.Empty) return false;
            if (thread.VendorId == userId) return true;
            var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
            return vendor is not null && thread.VendorId == vendor.Id;
        }

        public async Task<MessageResponse> OpenBookingThreadAsync(Guid customerId, Guid vendorUserId, Guid? rfpId, string note)
        {
            var now = DateTime.UtcNow;

            // One conversation per customer and vendor: the quote-request thread when the booking
            // came from one, otherwise their most recent thread.
            var candidates = _db.ChatThreads.Where(t => t.CustomerId == customerId && t.VendorId == vendorUserId);
            var thread = (rfpId.HasValue ? await candidates.FirstOrDefaultAsync(t => t.RfpId == rfpId) : null)
                         ?? await candidates.OrderByDescending(t => t.UpdatedAt).FirstOrDefaultAsync();

            if (thread is null)
            {
                thread = new ChatThread
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    VendorId = vendorUserId,
                    RfpId = rfpId,
                    UpdatedAt = now
                };
                _db.ChatThreads.Add(thread);
            }

            // A confirmed booking opens the conversation, whatever state an earlier request was in.
            thread.Status = ChatThreadStatus.Active.ToString();
            thread.LastMessage = note;
            thread.UpdatedAt = now;
            thread.UnreadCount += 1;

            var message = new Core.Entities.ChatMessage
            {
                Id = Guid.NewGuid(),
                ThreadId = thread.Id,
                SenderId = vendorUserId,
                Content = note,
                Timestamp = now
            };
            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();

            var sender = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vendorUserId);
            var vendorName = await _db.Vendors.AsNoTracking()
                .Where(v => v.UserId == vendorUserId)
                .Select(v => v.BusinessName)
                .FirstOrDefaultAsync();

            return new MessageResponse(
                message.Id.ToString(),
                thread.Id.ToString(),
                vendorUserId.ToString(),
                !string.IsNullOrWhiteSpace(vendorName) ? vendorName : sender?.Name ?? "",
                sender?.Avatar,
                message.Content,
                now);
        }

        public async Task<IReadOnlyList<Guid>> ParticipantUserIdsAsync(Guid threadId)
        {
            var thread = await _db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId);
            if (thread is null) return Array.Empty<Guid>();

            // Legacy rows may hold the Vendor profile id rather than the vendor's user id.
            var vendorUserId = await _db.Vendors.AsNoTracking()
                .Where(v => v.Id == thread.VendorId)
                .Select(v => (Guid?)v.UserId)
                .FirstOrDefaultAsync() ?? thread.VendorId;

            return new[] { thread.CustomerId, vendorUserId };
        }

        public async Task<bool> MarkAsReadAsync(Guid threadId, Guid userId)
        {
            var thread = await _db.ChatThreads.FindAsync(threadId);
            if (thread == null || !await IsParticipantAsync(threadId, userId)) return false;

            thread.UnreadCount = 0;
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
