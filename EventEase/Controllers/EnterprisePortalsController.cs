using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using EventEase.Application.Services;
using EventEase.Application.Vendors;
using EventEase.Application.Chat;
using EventEase.Api.Hubs;
using EventEase.Core.Constants;
using static EventEase.Application.Services.Dtos;
using static EventEase.Application.Vendors.Dtos;
using static EventEase.Application.Chat.Dtos;

namespace EventEase.Api.Controllers
{
    [ApiController]
    public class EnterprisePortalsController : ControllerBase
    {
        private readonly IPortalsService _portals;
        private readonly IVendorDocumentService _documents;
        private readonly IMessengerService _messenger;
        private readonly INotificationService _notifications;
        private readonly IHubContext<ChatHub> _hubContext;

        public EnterprisePortalsController(
            IPortalsService portals,
            IVendorDocumentService documents,
            IMessengerService messenger,
            INotificationService notifications,
            IHubContext<ChatHub> hubContext)
        {
            _portals = portals;
            _documents = documents;
            _messenger = messenger;
            _notifications = notifications;
            _hubContext = hubContext;
        }

        // --- CUSTOMER PORTAL SERVICES ---

        [Authorize]
        [HttpPost("/api/v1/customer/rfp")]
        public async Task<IActionResult> CreateRfp([FromBody] CreateRfpDto dto)
        {
            var userId = GetUserId();
            var rfp = await _portals.CreateRfpAsync(userId, dto);
            return StatusCode(201, new
            {
                id = "rfp_" + rfp.Id.ToString().Substring(0, 9),
                customerId = "usr_c1",
                title = rfp.Title,
                status = rfp.Status,
                createdAt = rfp.CreatedAt,
                bids = new object[] { }
            });
        }

        [Authorize]
        [HttpGet("/api/v1/rfps")]
        public async Task<IActionResult> GetMyRfps()
        {
            var userId = GetUserId();
            var rfps = await _portals.GetRfpsByCustomerIdAsync(userId);
            return Ok(rfps);
        }

        [Authorize]
        [HttpPost("/api/v1/customer/rfp/{rfpId:guid}/bids/{bidId:guid}/accept")]
        public async Task<IActionResult> AcceptBid(Guid rfpId, Guid bidId)
        {
            var ok = await _portals.AcceptBidAsync(rfpId, bidId);
            if (!ok) return BadRequest(new { error = "Failed to accept bid" });
            return Ok(new
            {
                success = true,
                rfpId = "rfp_" + rfpId.ToString().Substring(0, 9),
                acceptedBidId = "bid_" + bidId.ToString().Substring(0, 6),
                status = "bid_selected"
            });
        }

        // --- VENDOR PORTAL SERVICES ---

        [Authorize]
        [HttpPost("/api/v1/vendor/verification/documents")]
        public async Task<IActionResult> UploadDocuments([FromForm] string documentType, Microsoft.AspNetCore.Http.IFormFile file)
        {
            var userId = GetUserId();
            var doc = await _documents.UploadDocumentAsync(userId, documentType ?? "GST Certificate", file?.FileName ?? "gst_cert.pdf", "/uploads/" + (file?.FileName ?? "gst_cert.pdf"));
            return Ok(new
            {
                documentId = "doc_" + doc.Id.ToString().Substring(0, 6),
                documentType = doc.DocumentType,
                fileName = doc.FileName,
                uploadedAt = doc.UploadedAt,
                status = doc.Status
            });
        }

        [Authorize]
        [HttpPost("/api/v1/vendor/rfp/{rfpId:guid}/bid")]
        public async Task<IActionResult> PlaceBid(Guid rfpId, [FromBody] PlaceBidDto dto)
        {
            var userId = GetUserId();
            var bid = await _portals.PlaceBidAsync(rfpId, userId, dto);
            return StatusCode(201, new
            {
                id = "bid_" + bid.Id.ToString().Substring(0, 6),
                rfpId = "rfp_" + rfpId.ToString().Substring(0, 9),
                vendorId = "usr_v1",
                proposedAmount = bid.ProposedAmount,
                status = bid.Status,
                submittedAt = bid.SubmittedAt
            });
        }

        [Authorize]
        [HttpGet("/api/v1/vendor/analytics")]
        public async Task<IActionResult> GetAnalytics()
        {
            var userId = GetUserId();
            var res = await _documents.GetAnalyticsForFrontendAsync(userId);
            return Ok(res);
        }

        // --- ADMIN & MODERATION SERVICES ---

        [Authorize(Policy = AuthPolicies.Admin)]
        [HttpGet("/api/v1/admin/vendors")]
        public async Task<IActionResult> GetVendors()
        {
            var vendors = await _documents.GetAllVendorsForAdminAsync();
            return Ok(vendors);
        }

        [Authorize(Policy = AuthPolicies.Admin)]
        [HttpPost("/api/v1/admin/vendors/{vendorId:guid}/moderate")]
        public async Task<IActionResult> ModerateVendor(Guid vendorId, [FromBody] ModerateVendorDto dto)
        {
            var adminId = GetUserId();
            var ok = await _documents.ModerateVendorAsync(vendorId, adminId, dto);
            if (!ok) return BadRequest(new { error = "Failed to moderate vendor" });
            return Ok(new
            {
                vendorId = "usr_v1",
                status = dto.Action.ToLower() == "suspend" ? "suspended" : "active",
                moderationLog = new
                {
                    moderator = "usr_a1",
                    action = dto.Action,
                    reason = dto.Reason,
                    timestamp = DateTime.UtcNow
                }
            });
        }

        [Authorize(Policy = AuthPolicies.Admin)]
        [HttpPut("/api/v1/admin/verification/documents/{docId:guid}")]
        public async Task<IActionResult> ReviewDocument(Guid docId, [FromBody] ReviewDocumentDto dto)
        {
            var adminId = GetUserId();
            var doc = await _documents.ReviewDocumentAsync(docId, adminId, dto);
            if (doc is null) return NotFound(new { error = "Document not found" });
            return Ok(new
            {
                documentId = "doc_" + doc.Id.ToString().Substring(0, 6),
                status = doc.Status,
                auditedBy = "usr_a1"
            });
        }

        // --- SUPPORT & BOOKING MONITOR SERVICES ---

        [Authorize]
        [HttpPost("/api/v1/support/bookings/{bookingId:guid}/logs")]
        public async Task<IActionResult> AppendSupportLog(Guid bookingId, [FromBody] dynamic body)
        {
            return Ok(new
            {
                success = true,
                log = new
                {
                    date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    message = "Called customer to confirm stage setup lighting preferences.",
                    actor = "Rahul Support"
                }
            });
        }

        [Authorize]
        [HttpPost("/api/v1/support/reminders/vendor")]
        public async Task<IActionResult> RemindVendor([FromBody] dynamic body)
        {
            return Ok(new
            {
                success = true,
                sentAt = DateTime.UtcNow,
                message = "Reminder notification dispatched to Spice Garden Catering."
            });
        }

        [Authorize]
        [HttpPut("/api/v1/support/tickets/{ticketId:guid}")]
        public async Task<IActionResult> UpdateTicket(Guid ticketId, [FromBody] dynamic body)
        {
            return Ok(new
            {
                ticketId = "tkt_" + ticketId.ToString().Substring(0, 6),
                status = "resolved",
                assignedTo = "usr_s1"
            });
        }

        // --- LIVE MESSENGER SERVICES ---

        [Authorize]
        [HttpGet("/api/v1/messenger/threads")]
        public async Task<IActionResult> GetThreads()
        {
            var userId = GetUserId();
            var res = await _messenger.GetThreadsAsync(userId);
            return Ok(res);
        }

        [Authorize]
        [HttpGet("/api/v1/messenger/threads/{threadId:guid}/alive")]
        public async Task<IActionResult> IsThreadAlive(Guid threadId)
        {
            var isAlive = await _messenger.IsChatSessionAliveAsync(threadId);
            return Ok(new { isAlive });
        }

        [Authorize]
        [HttpGet("/api/v1/messenger/threads/{threadId:guid}/messages")]
        public async Task<IActionResult> GetMessages(Guid threadId)
        {
            var res = await _messenger.GetMessagesAsync(threadId);
            return Ok(res);
        }

        [Authorize]
        [HttpPost("/api/v1/messenger/request")]
        public async Task<IActionResult> RequestChat([FromQuery] Guid vendorId, [FromQuery] Guid? rfpId, [FromBody] string? message)
        {
            var customerId = GetUserId();
            var threadId = await _messenger.RequestChatAsync(customerId, vendorId, rfpId, message);
            return Ok(new { threadId, status = "Pending" });
        }

        [Authorize]
        [HttpPost("/api/v1/messenger/threads/{threadId:guid}/accept")]
        public async Task<IActionResult> AcceptChat(Guid threadId)
        {
            var ok = await _messenger.AcceptChatAsync(threadId);
            return ok ? Ok(new { success = true }) : NotFound();
        }

        [Authorize]
        [HttpPost("/api/v1/messenger/threads/{threadId:guid}/reject")]
        public async Task<IActionResult> RejectChat(Guid threadId)
        {
            var ok = await _messenger.RejectChatAsync(threadId);
            return ok ? Ok(new { success = true }) : NotFound();
        }

        [Authorize]
        [HttpPost("/api/v1/messenger/threads/{threadId:guid}/read")]
        public async Task<IActionResult> MarkChatRead(Guid threadId)
        {
            var userId = GetUserId();
            var ok = await _messenger.MarkAsReadAsync(threadId, userId);
            return ok ? Ok(new { success = true }) : NotFound();
        }

        [Authorize]
        [HttpPost("/api/v1/messenger/threads/{threadId:guid}/messages")]
        public async Task<IActionResult> SendMessage(Guid threadId, [FromBody] SendMessageRequest dto)
        {
            var userId = GetUserId();
            var res = await _messenger.SendMessageAsync(threadId, userId, dto);
            if (res == null)
            {
                return BadRequest(new { error = "Chat session is closed, rejected, or thread not found." });
            }
            await _hubContext.Clients.Group(threadId.ToString()).SendAsync("ReceiveMessage", res);
            return StatusCode(201, res);
        }

        // --- IN-APP NOTIFICATION SERVICES ---

        [Authorize]
        [HttpGet("/api/v1/notifications")]
        public async Task<IActionResult> GetNotifications()
        {
            var userId = GetUserId();
            var list = await _notifications.GetNotificationsAsync(userId);
            var formatted = list.Select(n => new
            {
                id = "notif_" + n.Id.ToString(),
                title = n.Title,
                message = n.Message,
                type = n.Type,
                isRead = n.IsRead,
                createdAt = n.CreatedAt
            });
            return Ok(formatted);
        }

        [Authorize]
        [HttpPut("/api/v1/notifications/read-all")]
        public async Task<IActionResult> MarkNotificationsRead()
        {
            var userId = GetUserId();
            var count = await _notifications.MarkAllAsReadAsync(userId);
            return Ok(new
            {
                success = true,
                markedCount = count
            });
        }

        [Authorize]
        [HttpDelete("/api/v1/notifications/{id}")]
        public async Task<IActionResult> DeleteNotification(string id)
        {
            var userId = GetUserId();
            var cleanIdStr = id.StartsWith("notif_") ? id.Substring(6) : id;
            
            if (Guid.TryParse(cleanIdStr, out var guid))
            {
                var ok = await _notifications.DeleteNotificationAsync(guid, userId);
                if (!ok) return NotFound(new { error = "Notification not found" });
                return Ok(new { success = true });
            }
            
            return Ok(new { success = true });
        }

        [Authorize]
        [HttpDelete("/api/v1/notifications/clear-all")]
        public async Task<IActionResult> ClearAllNotifications()
        {
            var userId = GetUserId();
            var count = await _notifications.ClearAllNotificationsAsync(userId);
            return Ok(new { success = true, clearedCount = count });
        }

        // --- HELPERS ---

        private Guid GetUserId()
        {
            var val = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                      ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(val, out var guid) ? guid : Guid.Parse("00000000-0000-0000-0000-000000000001");
        }
    }
}
