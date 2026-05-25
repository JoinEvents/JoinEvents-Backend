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
                .ToDictionaryAsync(u => u.Id, u => new { u.Name, Role = u.Role ?? "Customer" });

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
        public IActionResult GetPendingVendors()
        {
            return Ok(new List<object>
            {
                new { id = Guid.NewGuid(), businessName = "Mock Vendor 1", verificationStatus = "pending" }
            });
        }

        [HttpPost("vendors/{id:guid}/verify")]
        public IActionResult VerifyVendor(Guid id, [FromBody] object dto)
        {
            return Ok(new { id, verificationStatus = "verified" });
        }

        // --- Bookings ---
        [HttpGet("bookings")]
        public IActionResult GetBookings()
        {
            return Ok(new List<object>
            {
                new { id = Guid.NewGuid(), eventName = "Mock Wedding", status = "pending", customerName = "John Doe", city = "Delhi" }
            });
        }

        [HttpPost("bookings/{id:guid}/note")]
        public IActionResult AddBookingNote(Guid id, [FromBody] object dto)
        {
            return Ok(new { id, status = "pending" });
        }

        [HttpPost("bookings/{id:guid}/user-update")]
        public IActionResult UpdateBookingUser(Guid id, [FromBody] object dto)
        {
            return Ok(new { id, status = "pending" });
        }

        [HttpPost("bookings/{id:guid}/vendor-reminder")]
        public IActionResult RemindVendorBooking(Guid id, [FromBody] object dto)
        {
            return Ok(new { id, status = "pending" });
        }

        // --- Reviews ---
        [HttpGet("reviews/flagged")]
        public IActionResult GetFlaggedReviews()
        {
            return Ok(new List<object>());
        }

        [HttpPost("reviews/{id:guid}/moderate")]
        public IActionResult ModerateReview(Guid id, [FromBody] object dto)
        {
            return Ok(new { success = true });
        }
    }
}
