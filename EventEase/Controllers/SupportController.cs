using EventEase.Application.SupportTicket;
using EventEase.Application.Vendors;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using static EventEase.Application.SupportTicket.Dtos;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/support")]
    public class SupportController : ControllerBase
    {
        private readonly ISupportService _service;
        private readonly EventEaseDbContext _db;
        private readonly IFileStorage _fileStorage;

        public SupportController(ISupportService service, EventEaseDbContext db, IFileStorage fileStorage)
        {
            _service = service;
            _db = db;
            _fileStorage = fileStorage;
        }

        // --- Dashboard Stats ---
        [HttpGet("stats")]
        public async Task<IActionResult> GetDashboardStats()
        {
            var stats = await _service.GetDashboardStatsAsync();
            return Ok(stats);
        }

        // --- Tickets ---
        [Authorize(Policy = "User")]
        [HttpPost("ticket")]
        public async Task<IActionResult> Create([FromBody] CreateTicketDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty)
            {
                return Unauthorized(new { error = "Unauthorized", details = "User ID not found in token claims." });
            }
            var ticket = await _service.CreateAsync(userId, dto);

            // Save the initial ticket description as the first message in the message thread!
            if (!string.IsNullOrEmpty(dto.Description))
            {
                var firstMessage = new EventEase.Core.Entities.ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ThreadId = ticket.Id,
                    SenderId = userId,
                    Content = dto.Description,
                    Timestamp = DateTime.UtcNow,
                    IsInternal = false
                };
                _db.ChatMessages.Add(firstMessage);
                await _db.SaveChangesAsync();
            }

            return Ok(await MapToTicketResponseAsync(ticket, IsAgent()));
        }

        [HttpGet("tickets")]
        public async Task<IActionResult> GetAll()
        {
            var tickets = await _service.GetAllAsync();
            return Ok(await MapToTicketResponsesAsync(tickets, IsAgent()));
        }

        [Authorize(Policy = "User")]
        [HttpGet("my-tickets")]
        public async Task<IActionResult> GetMyTickets()
        {
            var userId = GetUserId();
            if (userId == Guid.Empty)
            {
                return Unauthorized(new { error = "Unauthorized", details = "User ID not found in token claims." });
            }
            var tickets = await _service.GetAllAsync();
            var userTickets = tickets.Where(t => t.UserId == userId).ToList();
            return Ok(await MapToTicketResponsesAsync(userTickets, IsAgent()));
        }

        private bool IsAgent()
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
            return !string.IsNullOrEmpty(role) && 
                   (role.Equals("Admin", StringComparison.OrdinalIgnoreCase) || 
                    role.Equals("Support", StringComparison.OrdinalIgnoreCase));
        }

        private Guid GetUserId()
        {
            var val = User.FindFirstValue(ClaimTypes.NameIdentifier) 
                      ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? User.FindFirstValue("sub")
                      ?? User.FindFirstValue("id");
            
            if (string.IsNullOrEmpty(val))
            {
                Serilog.Log.Warning("SupportController: User ID claim not found. Claims present: {Claims}", 
                    string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));
                return Guid.Empty;
            }
            
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }

        [HttpGet("tickets/{id:guid}")]
        public async Task<IActionResult> GetTicketById(Guid id)
        {
            var ticket = await _db.SupportTickets.FindAsync(id);
            if (ticket is null) return NotFound(new { error = "Ticket not found." });
            return Ok(await MapToTicketResponseAsync(ticket, IsAgent()));
        }

        public record SupportReplyDto(string Message, bool IsInternal);

        [Authorize(Policy = "User")]
        [HttpPost("tickets/{id:guid}/reply")]
        public async Task<IActionResult> ReplyToTicket(Guid id, [FromBody] SupportReplyDto dto)
        {
            var senderId = GetUserId();
            if (senderId == Guid.Empty)
            {
                return Unauthorized(new { error = "Unauthorized", details = "User ID not found in token claims." });
            }

            var ticket = await _db.SupportTickets.FindAsync(id);
            if (ticket is null) return NotFound(new { error = "Ticket not found." });

            var message = new EventEase.Core.Entities.ChatMessage
            {
                Id = Guid.NewGuid(),
                ThreadId = id,
                SenderId = senderId,
                Content = dto.Message,
                Timestamp = DateTime.UtcNow,
                IsInternal = dto.IsInternal
            };
            _db.ChatMessages.Add(message);

            if (ticket.Status.Equals("Open", StringComparison.OrdinalIgnoreCase))
            {
                ticket.Status = "InProgress";
                ticket.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();

            return Ok(await MapToTicketResponseAsync(ticket, IsAgent()));
        }

        [HttpPatch("tickets/{id:guid}/status")]
        public async Task<IActionResult> UpdateStatusPath(Guid id, [FromBody] UpdateTicketDto dto)
        {
            var ticket = await _service.UpdatePropertiesAsync(id, dto.Status, dto.Priority);
            return ticket is null ? NotFound() : Ok(await MapToTicketResponseAsync(ticket, IsAgent()));
        }

        [HttpPut("ticket/{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTicketDto dto)
        {
            var ticket = await _service.UpdatePropertiesAsync(id, dto.Status, dto.Priority);
            return ticket is null ? NotFound() : Ok(await MapToTicketResponseAsync(ticket, IsAgent()));
        }

        private async Task<object> MapToTicketResponseAsync(EventEase.Core.Entities.SupportTicket ticket, bool showInternal = false)
        {
            var messages = await _db.ChatMessages
                .Where(m => m.ThreadId == ticket.Id)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            var filteredMessages = messages.Where(m => showInternal || !m.IsInternal).ToList();

            var senderIds = filteredMessages.Select(m => m.SenderId).Distinct().ToList();
            var users = await _db.Users
                .Where(u => senderIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => new { u.Name, Role = u.Role ?? "Customer" });

            var messageResponses = filteredMessages.Select(m => new
            {
                Id = m.Id,
                ThreadId = m.ThreadId,
                SenderId = m.SenderId,
                SenderName = users.TryGetValue(m.SenderId, out var user) ? user.Name : "System User",
                SenderRole = users.TryGetValue(m.SenderId, out var uRole) ? uRole.Role.ToLower() : "customer",
                Content = m.Content,
                Timestamp = m.Timestamp,
                IsRead = true,
                Type = "text",
                IsInternal = m.IsInternal
            }).ToList();

            var customer = await _db.Users.FindAsync(ticket.UserId);
            var customerName = customer?.Name ?? "Customer";
            var customerAvatar = customer?.Avatar;

            object? vendorContact = null;
            Booking? booking = null;
            if (ticket.BookingId.HasValue && ticket.BookingId.Value != Guid.Empty)
            {
                booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == ticket.BookingId.Value);
            }
            else if (!string.IsNullOrEmpty(ticket.EventName))
            {
                booking = await _db.Bookings.FirstOrDefaultAsync(b => b.UserId == ticket.UserId && b.EventName == ticket.EventName);
            }

            if (booking != null)
            {
                var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.Id == booking.VendorId);
                if (vendor != null)
                {
                    var vendorUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == vendor.UserId);
                    vendorContact = new
                    {
                        BusinessName = vendor.BusinessName,
                        ContactName = vendorUser?.Name ?? "Vendor",
                        Email = vendorUser?.Email ?? "",
                        Phone = vendorUser?.Phone ?? ""
                    };
                }
            }

            object? bookingDetails = null;
            if (booking != null)
            {
                bookingDetails = new
                {
                    Id = booking.Id,
                    EventName = booking.EventName,
                    EventDate = booking.EventDate,
                    Status = booking.Status,
                    TotalAmount = booking.TotalAmount,
                    Venue = booking.Venue,
                    City = booking.City,
                    GuestCount = booking.GuestCount
                };
            }

            return new
            {
                Id = ticket.Id,
                CustomerId = ticket.UserId,
                CustomerName = customerName,
                CustomerAvatar = customerAvatar,
                Subject = ticket.Subject,
                Description = ticket.Description,
                Status = ticket.Status.ToLower(),
                Priority = ticket.Priority ?? "medium",
                EventName = ticket.EventName,
                CreatedAt = ticket.CreatedAt,
                UpdatedAt = ticket.UpdatedAt,
                Messages = messageResponses,
                VendorContact = vendorContact,
                AttachmentUrl = ticket.AttachmentUrl,
                BookingId = ticket.BookingId,
                BookingDetails = bookingDetails
            };
        }

        private async Task<List<object>> MapToTicketResponsesAsync(List<EventEase.Core.Entities.SupportTicket> tickets, bool showInternal = false)
        {
            var ticketIds = tickets.Select(t => t.Id).ToList();
            var customerIds = tickets.Select(t => t.UserId).Distinct().ToList();

            var allMessages = await _db.ChatMessages
                .Where(m => ticketIds.Contains(m.ThreadId))
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            var senderIds = allMessages.Select(m => m.SenderId).Distinct().ToList();
            var allUsers = await _db.Users
                .Where(u => senderIds.Contains(u.Id) || customerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => new { u.Name, Role = u.Role ?? "Customer", u.Avatar });

            var eventNames = tickets.Where(t => !string.IsNullOrEmpty(t.EventName)).Select(t => t.EventName).Distinct().ToList();
            var bookingIds = tickets.Where(t => t.BookingId.HasValue).Select(t => t.BookingId!.Value).ToList();
            var bookings = await _db.Bookings
                .Where(b => bookingIds.Contains(b.Id) || (customerIds.Contains(b.UserId) && eventNames.Contains(b.EventName)))
                .ToListAsync();

            var vendorIds = bookings.Select(b => b.VendorId).Distinct().ToList();
            var vendors = await _db.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToListAsync();

            var vendorUserIds = vendors.Select(v => v.UserId).Distinct().ToList();
            var vendorUsers = await _db.Users
                .Where(u => vendorUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u);

            var vendorsMap = vendors.ToDictionary(v => v.Id, v => v);

            var responses = new List<object>();
            foreach (var ticket in tickets)
            {
                var ticketMessages = allMessages
                    .Where(m => m.ThreadId == ticket.Id && (showInternal || !m.IsInternal))
                    .Select(m => new
                    {
                        Id = m.Id,
                        ThreadId = m.ThreadId,
                        SenderId = m.SenderId,
                        SenderName = allUsers.TryGetValue(m.SenderId, out var user) ? user.Name : "System User",
                        SenderRole = allUsers.TryGetValue(m.SenderId, out var uRole) ? uRole.Role.ToLower() : "customer",
                        Content = m.Content,
                        Timestamp = m.Timestamp,
                        IsRead = true,
                        Type = "text",
                        IsInternal = m.IsInternal
                    }).ToList();

                var customerName = allUsers.TryGetValue(ticket.UserId, out var cust) ? cust.Name : "Customer";
                var customerAvatar = allUsers.TryGetValue(ticket.UserId, out var custAv) ? custAv.Avatar : null;

                object? vendorContact = null;
                Booking? booking = null;
                if (ticket.BookingId.HasValue && ticket.BookingId.Value != Guid.Empty)
                {
                    booking = bookings.FirstOrDefault(b => b.Id == ticket.BookingId.Value);
                }
                else if (!string.IsNullOrEmpty(ticket.EventName))
                {
                    booking = bookings.FirstOrDefault(b => b.UserId == ticket.UserId && b.EventName == ticket.EventName);
                }

                if (booking != null)
                {
                    if (vendorsMap.TryGetValue(booking.VendorId, out var vendor))
                    {
                        vendorUsers.TryGetValue(vendor.UserId, out var vendorUser);
                        vendorContact = new
                        {
                            BusinessName = vendor.BusinessName,
                            ContactName = vendorUser?.Name ?? "Vendor",
                            Email = vendorUser?.Email ?? "",
                            Phone = vendorUser?.Phone ?? ""
                        };
                    }
                }

                object? bookingDetails = null;
                if (booking != null)
                {
                    bookingDetails = new
                    {
                        Id = booking.Id,
                        EventName = booking.EventName,
                        EventDate = booking.EventDate,
                        Status = booking.Status,
                        TotalAmount = booking.TotalAmount,
                        Venue = booking.Venue,
                        City = booking.City,
                        GuestCount = booking.GuestCount
                    };
                }

                responses.Add(new
                {
                    Id = ticket.Id,
                    CustomerId = ticket.UserId,
                    CustomerName = customerName,
                    CustomerAvatar = customerAvatar,
                    Subject = ticket.Subject,
                    Description = ticket.Description,
                    Status = ticket.Status.ToLower(),
                    Priority = ticket.Priority ?? "medium",
                    EventName = ticket.EventName,
                    CreatedAt = ticket.CreatedAt,
                    UpdatedAt = ticket.UpdatedAt,
                    Messages = ticketMessages,
                    VendorContact = vendorContact,
                    AttachmentUrl = ticket.AttachmentUrl,
                    BookingId = ticket.BookingId,
                    BookingDetails = bookingDetails
                });
            }

            return responses;
        }

        [Authorize(Policy = "User")]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadAttachment(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded." });
            }
            var urlPath = await _fileStorage.SaveAsync("support", file.FileName, file.OpenReadStream(), file.ContentType);
            return Ok(new { url = urlPath });
        }

        // --- Vendors ---
        [HttpGet("vendors/pending")]
        public async Task<IActionResult> GetPendingVendors()
        {
            var vendors = await _db.Vendors
                .Where(v => !v.IsValidated)
                .ToListAsync();

            var userIds = vendors.Select(v => v.UserId).ToList();
            var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u);

            var vendorIds = vendors.Select(v => v.Id).ToList();
            var docs = await _db.VendorDocuments.Where(d => vendorIds.Contains(d.VendorId)).ToListAsync();

            var allReviews = await _db.Reviews
                .Where(r => vendorIds.Contains(r.VendorId) && r.Status != "removed")
                .ToListAsync();

            var allBookingEarnings = await _db.Bookings
                .Where(b => vendorIds.Contains(b.VendorId) && (b.Status == "Paid" || b.Status == "confirmed" || b.Status == "completed"))
                .GroupBy(b => b.VendorId)
                .Select(g => new { VendorId = g.Key, Total = g.Sum(b => b.TotalAmount) })
                .ToDictionaryAsync(x => x.VendorId, x => x.Total);

            var response = vendors.Select(v => {
                users.TryGetValue(v.UserId, out var u);
                var vDocs = docs.Where(d => d.VendorId == v.Id).ToList();
                var vReviews = allReviews.Where(r => r.VendorId == v.Id).ToList();
                var avgRating = vReviews.Count > 0 ? Math.Round(vReviews.Average(r => r.Rating), 1) : 0;
                allBookingEarnings.TryGetValue(v.Id, out var earnings);

                return new {
                    id = v.Id,
                    name = u?.Name ?? "Unknown",
                    businessName = v.BusinessName,
                    email = u?.Email ?? "Unknown",
                    phone = u?.Phone ?? "Unknown",
                    avatar = u?.Avatar,
                    city = v.Location,
                    services = v.services?.Select(s => s.Name).ToList() ?? new List<string>(),
                    isVerified = v.IsValidated,
                    verificationStatus = vDocs.Any(d => d.Status == "pending") ? "under_review" : "pending",
                    verificationDocs = vDocs.Select(d => new {
                        type = d.DocumentType,
                        name = d.FileName,
                        uploadedAt = d.UploadedAt.ToString("yyyy-MM-dd"),
                        status = d.Status,
                        fileUrl = d.FileUrl,
                        url = d.FileUrl
                    }).ToList(),
                    rating = avgRating,
                    totalReviews = vReviews.Count,
                    totalEarnings = earnings,
                    joinedDate = v.CreatedAt.ToString("yyyy-MM-dd"),
                    accountStatus = "active"
                };
            }).ToList();

            return Ok(response);
        }

        public class VerifyVendorDto
        {
            public string Status { get; set; } = string.Empty;
            public string? Remarks { get; set; }
        }

        [HttpPost("vendors/{id:guid}/verify")]
        public async Task<IActionResult> VerifyVendor(Guid id, [FromBody] VerifyVendorDto dto)
        {
            var vendor = await _db.Vendors.FindAsync(id);
            if (vendor == null) return NotFound(new { error = "Vendor not found" });

            if (dto.Status.Equals("verified", StringComparison.OrdinalIgnoreCase) || dto.Status.Equals("approved", StringComparison.OrdinalIgnoreCase))
            {
                vendor.IsValidated = true;
                var docs = await _db.VendorDocuments.Where(d => d.VendorId == id && d.Status == "pending").ToListAsync();
                foreach(var doc in docs) { doc.Status = "approved"; }
            }
            else if (dto.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase) || dto.Status.Equals("action_required", StringComparison.OrdinalIgnoreCase))
            {
                var docs = await _db.VendorDocuments.Where(d => d.VendorId == id && d.Status == "pending").ToListAsync();
                foreach(var doc in docs) { 
                    doc.Status = dto.Status.ToLower(); 
                    doc.RejectionReason = dto.Remarks;
                }
            }
            
            await _db.SaveChangesAsync();

            try
            {
                _db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = vendor.UserId,
                    Title = vendor.IsValidated ? "Profile Verified 🎉" : "Verification Update ⚠️",
                    Message = vendor.IsValidated ? "Your vendor profile has been verified!" : $"Your profile verification was updated. Remarks: {dto.Remarks}",
                    Type = "verification",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to send notification for vendor verification");
            }

            return Ok(new { id, verificationStatus = vendor.IsValidated ? "verified" : dto.Status });
        }

        // --- Bookings ---
        [HttpGet("bookings")]
        public async Task<IActionResult> GetBookings()
        {
            var bookings = await _db.Bookings.ToListAsync();
            var result = new List<object>();
            foreach (var b in bookings)
            {
                result.Add(await MapBookingToDto(b));
            }
            return Ok(result);
        }

        private async Task<object> MapBookingToDto(Booking b)
        {
            var user = await _db.Users.FindAsync(b.UserId);
            var customerName = user?.Name ?? "Customer";
            var customerPhone = user?.Phone ?? "";
            
            var vendor = await _db.Vendors.FindAsync(b.VendorId);
            var vendorName = vendor?.BusinessName ?? "Vendor Partner";

            string mappedStatus = b.Status.ToLower();
            if (mappedStatus == "paid") mappedStatus = "confirmed";

            var services = new List<object>();

            var package = b.PackageId.HasValue 
                ? await _db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == b.PackageId.Value) 
                : null;

            if (package != null && package.Includes != null)
            {
                foreach (var include in package.Includes)
                {
                    services.Add(new
                    {
                        serviceId = Guid.NewGuid().ToString(),
                        serviceName = include,
                        category = "Included Service",
                        vendorId = b.VendorId.ToString(),
                        vendorName = vendorName,
                        price = 0m,
                        status = "included"
                    });
                }
            }

            // Fetch review
            var review = await _db.Reviews.FirstOrDefaultAsync(r => r.BookingId == b.Id && r.Status != "removed");
            object? reviewInfo = null;
            if (review != null)
            {
                reviewInfo = new
                {
                    rating = review.Rating,
                    comment = review.Comment
                };
            }

            // Fetch dispute info
            object? disputeInfo = null;
            if (mappedStatus == "disputed")
            {
                var disputeLog = await _db.BookingLogs
                    .Where(l => l.BookingId == b.Id && l.Message.StartsWith("Dispute raised. Reason:"))
                    .OrderByDescending(l => l.CreatedAt)
                    .FirstOrDefaultAsync();

                var reason = disputeLog != null 
                    ? disputeLog.Message.Substring("Dispute raised. Reason:".Length).Trim()
                    : "Dispute raised.";

                disputeInfo = new
                {
                    reason = reason,
                    status = "open"
                };
            }

            // Map eventTypeId
            string eventTypeId = "wedding";
            var nameLower = (b.EventName ?? "").ToLower();
            if (nameLower.Contains("birthday")) eventTypeId = "birthday";
            else if (nameLower.Contains("corporate")) eventTypeId = "corporate";
            else if (nameLower.Contains("beauty")) eventTypeId = "beauty";
            else if (nameLower.Contains("travel")) eventTypeId = "travel";
            else if (nameLower.Contains("shopping")) eventTypeId = "shopping";

            // Fetch support logs
            var logs = await _db.BookingLogs
                .Where(l => l.BookingId == b.Id)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            var supportLogs = logs.Select(l => new
            {
                message = l.Message,
                actor = l.Actor,
                date = l.CreatedAt.ToString("yyyy-MM-dd HH:mm")
            }).ToList();

            // Resolve assignedTo from the latest assignment log
            var assignedLog = logs.FirstOrDefault(l => l.Message.StartsWith("Assigned to:"));
            var assignedTo = assignedLog?.Message.Replace("Assigned to:", "").Trim();

            return new
            {
                id = b.Id.ToString(),
                bookingNumber = $"BK-{b.Id.ToString().Substring(0, 8).ToUpper()}",
                customerId = b.UserId.ToString(),
                customerName = customerName,
                customerPhone = customerPhone,
                eventTypeId = eventTypeId,
                eventName = b.EventName,
                packageId = b.PackageId?.ToString(),
                packageName = b.PackageName,
                eventDate = b.EventDate.ToString("yyyy-MM-dd"),
                venue = b.Venue,
                city = b.City,
                guestCount = b.GuestCount,
                status = mappedStatus,
                advanceAmount = b.AdvanceAmount,
                baseAmount = Math.Round((b.TotalAmount - b.DamageCharges) / 1.18m, 2),
                extraServicesAmount = b.ExtraServicesAmount,
                damageCharges = b.DamageCharges,
                damageChargeNotes = b.DamageChargeNotes,
                isDamageChargeApproved = b.IsDamageChargeApproved,
                gstPercent = 18,
                totalAmount = b.TotalAmount,
                finalPaidAmount = b.FinalPaidAmount,
                cancelledBy = b.CancelledBy,
                cancellationReason = b.CancellationReason,
                assignedTo = assignedTo,
                disputeInfo = disputeInfo,
                review = reviewInfo,
                services = services,
                supportLogs = supportLogs,
                createdAt = b.EventDate.AddDays(-30).ToString("yyyy-MM-dd HH:mm")
            };
        }

        // --- Booking Notes (real DB) ---
        public class BookingNoteDto { public string Note { get; set; } = ""; }

        [HttpPost("bookings/{id:guid}/note")]
        public async Task<IActionResult> AddBookingNote(Guid id, [FromBody] BookingNoteDto dto)
        {
            var booking = await _db.Bookings.FindAsync(id);
            if (booking == null) return NotFound(new { error = "Booking not found" });

            var log = new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = id,
                Message = dto.Note,
                Actor = User.FindFirstValue(ClaimTypes.Name) ?? "Support Agent",
                CreatedAt = DateTime.UtcNow
            };
            _db.BookingLogs.Add(log);
            await _db.SaveChangesAsync();

            return Ok(new { id, status = booking.Status.ToLower(), logId = log.Id });
        }

        // --- Notify Customer (real DB) ---
        public class UserUpdateDto { public string Message { get; set; } = ""; }

        [HttpPost("bookings/{id:guid}/user-update")]
        public async Task<IActionResult> UpdateBookingUser(Guid id, [FromBody] UserUpdateDto dto)
        {
            var booking = await _db.Bookings.FindAsync(id);
            if (booking == null) return NotFound(new { error = "Booking not found" });

            // Log it
            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = id,
                Message = $"Customer notified: {dto.Message}",
                Actor = User.FindFirstValue(ClaimTypes.Name) ?? "Support Agent",
                CreatedAt = DateTime.UtcNow
            });

            // Send notification to the customer
            _db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = booking.UserId,
                Title = "Booking Update 📋",
                Message = dto.Message,
                Type = "booking",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return Ok(new { id, status = booking.Status.ToLower(), notified = true });
        }

        // --- Remind Vendor (real DB) ---
        public class VendorReminderDto { public string VendorId { get; set; } = ""; }

        [HttpPost("bookings/{id:guid}/vendor-reminder")]
        public async Task<IActionResult> RemindVendorBooking(Guid id, [FromBody] VendorReminderDto dto)
        {
            var booking = await _db.Bookings.FindAsync(id);
            if (booking == null) return NotFound(new { error = "Booking not found" });

            // Resolve vendor user
            Guid vendorEntityId = Guid.Empty;
            if (!string.IsNullOrEmpty(dto.VendorId) && Guid.TryParse(dto.VendorId, out var parsed))
                vendorEntityId = parsed;
            else
                vendorEntityId = booking.VendorId;

            var vendor = await _db.Vendors.FindAsync(vendorEntityId);
            if (vendor == null) return NotFound(new { error = "Vendor not found" });

            // Send notification to vendor's user account
            _db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = vendor.UserId,
                Title = "Booking Reminder ⏰",
                Message = $"Reminder for booking {booking.EventName} on {booking.EventDate:yyyy-MM-dd}. Please confirm your availability.",
                Type = "booking",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            // Log the reminder
            _db.BookingLogs.Add(new BookingLog
            {
                Id = Guid.NewGuid(),
                BookingId = id,
                Message = $"Vendor reminder sent to {vendor.BusinessName}.",
                Actor = User.FindFirstValue(ClaimTypes.Name) ?? "Support Agent",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return Ok(new { id, status = booking.Status.ToLower(), reminded = true });
        }

        // --- Reviews (real DB) ---
        [HttpGet("reviews/flagged")]
        public async Task<IActionResult> GetFlaggedReviews()
        {
            var flaggedReviews = await _db.Reviews
                .Where(r => r.Status == "flagged")
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var userIds = flaggedReviews.Select(r => r.UserId).Distinct().ToList();
            var vendorIds = flaggedReviews.Select(r => r.VendorId).Distinct().ToList();
            var bookingIds = flaggedReviews.Select(r => r.BookingId).Distinct().ToList();

            var users = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u);

            var vendors = await _db.Vendors
                .Where(v => vendorIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v);

            var bookings = await _db.Bookings
                .Where(b => bookingIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b);

            var response = flaggedReviews.Select(r =>
            {
                users.TryGetValue(r.UserId, out var customer);
                vendors.TryGetValue(r.VendorId, out var vendor);
                bookings.TryGetValue(r.BookingId, out var booking);

                return new
                {
                    id = r.Id,
                    bookingId = r.BookingId,
                    vendorId = r.VendorId,
                    vendorName = vendor?.BusinessName ?? "Vendor",
                    customerId = r.UserId,
                    customerName = customer?.Name ?? r.CustomerName,
                    eventName = booking?.EventName ?? r.EventName,
                    rating = r.Rating,
                    comment = r.Comment,
                    status = r.Status,
                    disputeReason = r.DisputeReason,
                    createdAt = r.CreatedAt
                };
            }).ToList();

            return Ok(response);
        }

        public class ModerateReviewDto { public string Action { get; set; } = "keep"; }

        [HttpPost("reviews/{id:guid}/moderate")]
        public async Task<IActionResult> ModerateReview(Guid id, [FromBody] ModerateReviewDto dto)
        {
            var review = await _db.Reviews.FindAsync(id);
            if (review == null) return NotFound(new { error = "Review not found" });

            if (dto.Action.Equals("remove", StringComparison.OrdinalIgnoreCase))
            {
                review.Status = "removed";
            }
            else
            {
                // "keep" — restore to published and clear dispute
                review.Status = "published";
                review.DisputeReason = null;
            }

            await _db.SaveChangesAsync();

            // Notify the review author
            try
            {
                var message = dto.Action.Equals("remove", StringComparison.OrdinalIgnoreCase)
                    ? "Your review has been removed after moderation."
                    : "Your flagged review has been reviewed and kept published.";

                _db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = review.UserId,
                    Title = "Review Moderation Update",
                    Message = message,
                    Type = "general",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to send review moderation notification");
            }

            return Ok(new { success = true, id, status = review.Status });
        }
    }
}
