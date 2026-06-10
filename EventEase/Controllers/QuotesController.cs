using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/quotes")]
    [Authorize]
    public class QuotesController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public QuotesController(EventEaseDbContext db)
        {
            _db = db;
        }

        // Requests Payload
        public class CreateRfpRequest
        {
            public string Title { get; set; } = string.Empty;
            public string EventTypeId { get; set; } = string.Empty;
            public string EventTypeName { get; set; } = string.Empty;
            public DateTime EventDate { get; set; }
            public string City { get; set; } = string.Empty;
            public string VenueStatus { get; set; } = "not_booked"; // booked, not_booked
            public string? VenueName { get; set; }
            public string? Locality { get; set; }
            public string? Pincode { get; set; }
            public int GuestCount { get; set; }
            public decimal BudgetMin { get; set; }
            public decimal BudgetMax { get; set; }
            public string Requirements { get; set; } = string.Empty;
            public List<string> ServicesNeeded { get; set; } = new();
        }

        public class CreateBidRequest
        {
            public decimal ProposedAmount { get; set; }
            public string Description { get; set; } = string.Empty;
            public List<string> Deliverables { get; set; } = new();
            public DateTime ValidUntil { get; set; }
        }

        // Helpers
        private Guid GetUserId()
        {
            var val = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                      ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }

        private async Task<object> MapRfpToDtoAsync(Rfp rfp)
        {
            var user = await _db.Users.FindAsync(rfp.CustomerId);
            var customerName = user?.Name ?? "Customer";

            // Load bids
            var bids = await _db.Bids.Where(b => b.RfpId == rfp.Id).ToListAsync();
            var mappedBids = new List<object>();
            
            foreach (var bid in bids)
            {
                var vendor = await _db.Vendors.FindAsync(bid.VendorId);
                var vendorUser = vendor != null ? await _db.Users.FindAsync(vendor.UserId) : null;
                var vendorName = vendorUser?.Name ?? "Vendor Partner";
                var businessName = vendor?.BusinessName ?? "Vendor Business";
                
                mappedBids.Add(new
                {
                    id = bid.Id.ToString(),
                    rfpId = bid.RfpId.ToString(),
                    vendorId = bid.VendorId.ToString(),
                    vendorName = vendorName,
                    vendorBusinessName = businessName,
                    vendorRating = 4.8,
                    vendorReviews = 12,
                    isVerified = true,
                    proposedAmount = bid.ProposedAmount,
                    description = bid.Description,
                    deliverables = System.Text.Json.JsonSerializer.Deserialize<List<string>>(bid.DeliverablesJson ?? "[]") ?? new List<string>(),
                    validUntil = bid.ValidUntil.ToString("yyyy-MM-dd"),
                    submittedAt = bid.SubmittedAt.ToString("yyyy-MM-dd HH:mm"),
                    status = bid.Status.ToLower()
                });
            }

            var servicesNeeded = System.Text.Json.JsonSerializer.Deserialize<List<string>>(rfp.ServicesNeededJson ?? "[]") ?? new List<string>();

            return new
            {
                id = rfp.Id.ToString(),
                customerId = rfp.CustomerId.ToString(),
                customerName = customerName,
                title = rfp.Title,
                eventTypeId = rfp.EventTypeId,
                eventTypeName = rfp.EventTypeName,
                eventDate = rfp.EventDate.ToString("yyyy-MM-dd"),
                city = rfp.City,
                venueStatus = rfp.VenueStatus,
                venueName = rfp.VenueName ?? "",
                locality = rfp.Locality ?? "",
                pincode = rfp.Pincode ?? "",
                guestCount = rfp.GuestCount,
                budgetMin = rfp.BudgetMin,
                budgetMax = rfp.BudgetMax,
                requirements = rfp.Requirements,
                servicesNeeded = servicesNeeded,
                status = rfp.Status.ToLower(),
                createdAt = rfp.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                expiresAt = rfp.ExpiresAt.ToString("yyyy-MM-dd HH:mm"),
                bids = mappedBids
            };
        }

        // GET /api/v1/quotes
        [HttpGet]
        public async Task<IActionResult> GetQuotes([FromQuery] string? customerId)
        {
            Guid targetCustomerId = Guid.Empty;
            if (!string.IsNullOrEmpty(customerId))
            {
                Guid.TryParse(customerId, out targetCustomerId);
            }
            else
            {
                targetCustomerId = GetUserId();
            }

            var rfps = await _db.Rfps
                .Where(r => r.CustomerId == targetCustomerId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var dtos = new List<object>();
            foreach (var rfp in rfps)
            {
                dtos.Add(await MapRfpToDtoAsync(rfp));
            }

            return Ok(new { success = true, data = dtos });
        }

        // GET /api/v1/quotes/open
        [HttpGet("open")]
        public async Task<IActionResult> GetOpenQuotes()
        {
            var rfps = await _db.Rfps
                .Where(r => r.Status == "open" || r.Status == "receiving_bids")
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var dtos = new List<object>();
            foreach (var rfp in rfps)
            {
                dtos.Add(await MapRfpToDtoAsync(rfp));
            }

            return Ok(new { success = true, data = dtos });
        }

        // GET /api/v1/quotes/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetQuoteDetails(Guid id)
        {
            var rfp = await _db.Rfps.FindAsync(id);
            if (rfp == null)
            {
                return NotFound(new { success = false, message = "Quote request not found." });
            }

            var dto = await MapRfpToDtoAsync(rfp);
            return Ok(new { success = true, data = dto });
        }

        // POST /api/v1/quotes
        [HttpPost]
        public async Task<IActionResult> CreateQuote([FromBody] CreateRfpRequest req)
        {
            var customerId = GetUserId();
            if (customerId == Guid.Empty)
            {
                return Unauthorized(new { success = false, message = "User is not authenticated." });
            }

            // Validation checks
            if (req.EventDate < DateTime.Today.AddDays(7))
            {
                return BadRequest(new { success = false, message = "Event date must be at least 7 days in the future." });
            }
            if (req.VenueStatus == "booked" && string.IsNullOrWhiteSpace(req.VenueName))
            {
                return BadRequest(new { success = false, message = "Venue name / landmark is required when venue is booked." });
            }
            if (req.VenueStatus == "not_booked" && string.IsNullOrWhiteSpace(req.Locality))
            {
                return BadRequest(new { success = false, message = "Preferred locality / area is required when venue is not booked." });
            }

            var rfp = new Rfp
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                Title = req.Title,
                EventTypeId = req.EventTypeId,
                EventTypeName = req.EventTypeName,
                EventDate = req.EventDate,
                City = req.City,
                VenueStatus = req.VenueStatus,
                VenueName = req.VenueName,
                Locality = req.Locality,
                Pincode = req.Pincode,
                GuestCount = req.GuestCount,
                BudgetMin = req.BudgetMin,
                BudgetMax = req.BudgetMax,
                Requirements = req.Requirements,
                Status = "open",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                ServicesNeededJson = System.Text.Json.JsonSerializer.Serialize(req.ServicesNeeded)
            };

            _db.Rfps.Add(rfp);
            await _db.SaveChangesAsync();

            var dto = await MapRfpToDtoAsync(rfp);
            return Ok(new { success = true, data = dto });
        }

        // PUT /api/v1/quotes/{id}
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateQuote(Guid id, [FromBody] CreateRfpRequest req)
        {
            var rfp = await _db.Rfps.FindAsync(id);
            if (rfp == null)
            {
                return NotFound(new { success = false, message = "Quote request not found." });
            }

            var customerId = GetUserId();
            if (rfp.CustomerId != customerId)
            {
                return Forbid();
            }

            rfp.Title = req.Title;
            rfp.EventTypeId = req.EventTypeId;
            rfp.EventTypeName = req.EventTypeName;
            rfp.EventDate = req.EventDate;
            rfp.City = req.City;
            rfp.VenueStatus = req.VenueStatus;
            rfp.VenueName = req.VenueName;
            rfp.Locality = req.Locality;
            rfp.Pincode = req.Pincode;
            rfp.GuestCount = req.GuestCount;
            rfp.BudgetMin = req.BudgetMin;
            rfp.BudgetMax = req.BudgetMax;
            rfp.Requirements = req.Requirements;
            rfp.ServicesNeededJson = System.Text.Json.JsonSerializer.Serialize(req.ServicesNeeded);

            await _db.SaveChangesAsync();

            var dto = await MapRfpToDtoAsync(rfp);
            return Ok(new { success = true, data = dto });
        }

        // DELETE /api/v1/quotes/{id}
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteQuote(Guid id)
        {
            var rfp = await _db.Rfps.FindAsync(id);
            if (rfp == null)
            {
                return NotFound(new { success = false, message = "Quote request not found." });
            }

            var customerId = GetUserId();
            if (rfp.CustomerId != customerId)
            {
                return Forbid();
            }

            // Remove bids associated
            var bids = await _db.Bids.Where(b => b.RfpId == id).ToListAsync();
            _db.Bids.RemoveRange(bids);

            _db.Rfps.Remove(rfp);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, data = true });
        }

        // POST /api/v1/quotes/{rfpId}/offers
        [HttpPost("{rfpId:guid}/offers")]
        public async Task<IActionResult> SubmitOffer(Guid rfpId, [FromBody] CreateBidRequest req)
        {
            var rfp = await _db.Rfps.FindAsync(rfpId);
            if (rfp == null)
            {
                return NotFound(new { success = false, message = "Quote request not found." });
            }

            var userId = GetUserId();
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null)
            {
                return BadRequest(new { success = false, message = "Only registered vendors can submit offers." });
            }

            var bid = new Bid
            {
                Id = Guid.NewGuid(),
                RfpId = rfpId,
                VendorId = vendor.Id,
                ProposedAmount = req.ProposedAmount,
                Description = req.Description,
                ValidUntil = req.ValidUntil,
                Status = "pending",
                SubmittedAt = DateTime.UtcNow,
                DeliverablesJson = System.Text.Json.JsonSerializer.Serialize(req.Deliverables)
            };

            _db.Bids.Add(bid);
            rfp.Status = "receiving_bids";

            await _db.SaveChangesAsync();

            var vendorUser = await _db.Users.FindAsync(vendor.UserId);
            var vendorName = vendorUser?.Name ?? "Vendor Partner";

            var dto = new
            {
                id = bid.Id.ToString(),
                rfpId = bid.RfpId.ToString(),
                vendorId = bid.VendorId.ToString(),
                vendorName = vendorName,
                vendorBusinessName = vendor.BusinessName,
                vendorRating = 4.8,
                vendorReviews = 12,
                isVerified = true,
                proposedAmount = bid.ProposedAmount,
                description = bid.Description,
                deliverables = req.Deliverables,
                validUntil = bid.ValidUntil.ToString("yyyy-MM-dd"),
                submittedAt = bid.SubmittedAt.ToString("yyyy-MM-dd HH:mm"),
                status = bid.Status.ToLower()
            };

            return Ok(new { success = true, data = dto });
        }

        // POST /api/v1/quotes/{rfpId}/offers/{offerId}/accept
        [HttpPost("{rfpId:guid}/offers/{offerId:guid}/accept")]
        public async Task<IActionResult> AcceptOffer(Guid rfpId, Guid offerId)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var rfp = await _db.Rfps.FindAsync(rfpId);
                if (rfp == null)
                {
                    return NotFound(new { success = false, message = "Quote request not found." });
                }

                var customerId = GetUserId();
                if (rfp.CustomerId != customerId)
                {
                    return Forbid();
                }

                var bids = await _db.Bids.Where(b => b.RfpId == rfpId).ToListAsync();
                var selectedBid = bids.FirstOrDefault(b => b.Id == offerId);
                if (selectedBid == null)
                {
                    return NotFound(new { success = false, message = "Offer not found." });
                }

                // Accept chosen, reject others
                foreach (var bid in bids)
                {
                    bid.Status = bid.Id == offerId ? "accepted" : "rejected";
                }

                rfp.Status = "bid_selected";

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { success = true, data = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { success = false, message = "Error accepting offer: " + ex.Message });
            }
        }
    }
}
