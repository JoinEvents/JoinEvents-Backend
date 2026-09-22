using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/guarantee")]
    public class GuaranteeController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public GuaranteeController(EventEaseDbContext db)
        {
            _db = db;
        }

        private Guid GetUserId()
        {
            var val = User.FindFirstValue(ClaimTypes.NameIdentifier) 
                      ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? User.FindFirstValue("sub")
                      ?? User.FindFirstValue("id");
            return Guid.TryParse(val, out var guid) ? guid : Guid.Empty;
        }

        public class GuaranteePolicy
        {
            public string name { get; set; }
            public string description { get; set; }
            public string coverageType { get; set; } // no_show, quality, cancellation, escrow
            public decimal refundPercentage { get; set; }
            public decimal compensationAmount { get; set; }
            public bool mediationRequired { get; set; }
            public int timelineHours { get; set; }
        }

        /// <summary>
        /// The wire shape of a claim. Lowercase members, because the API
        /// serialises with PropertyNamingPolicy = null and both apps already
        /// read these names.
        /// </summary>
        public class GuaranteeClaimResponse
        {
            public string id { get; set; }
            public string bookingId { get; set; }
            public string customerId { get; set; }
            public string vendorId { get; set; }
            public string claimType { get; set; } // no_show, quality, cancellation
            public string reason { get; set; }
            public List<string> evidence { get; set; } = new List<string>();
            public string status { get; set; } // submitted, under_review, approved, rejected, resolved
            public decimal? refundAmount { get; set; }
            public decimal? compensationAmount { get; set; }
            public string submittedAt { get; set; }
            public string? resolvedAt { get; set; }
            public string? resolution { get; set; }
        }

        public class ClaimRequest
        {
            public string bookingId { get; set; }
            public string claimType { get; set; }
            public string reason { get; set; }
            public List<string>? evidence { get; set; }
        }

        [HttpGet("policies")]
        public IActionResult GetPolicies()
        {
            var policies = new List<GuaranteePolicy>
            {
                new GuaranteePolicy
                {
                    name = "Escrow Protection",
                    description = "Payment held securely and released after event completion.",
                    coverageType = "escrow",
                    refundPercentage = 100,
                    compensationAmount = 0,
                    mediationRequired = false,
                    timelineHours = 0
                },
                new GuaranteePolicy
                {
                    name = "Vendor No-Show Protection",
                    description = "Full refund plus ₹10,000 compensation for confirmed vendor no-shows.",
                    coverageType = "no_show",
                    refundPercentage = 100,
                    compensationAmount = 10000,
                    mediationRequired = false,
                    timelineHours = 48
                },
                new GuaranteePolicy
                {
                    name = "Service Quality Guarantee",
                    description = "Up to 50% refund after mediation for significant service quality issues.",
                    coverageType = "quality",
                    refundPercentage = 50,
                    compensationAmount = 0,
                    mediationRequired = true,
                    timelineHours = 168
                }
            };

            return Ok(new { success = true, data = policies });
        }

        [Authorize(Policy = "User")]
        [HttpPost("claim")]
        public async Task<IActionResult> SubmitClaim([FromBody] ClaimRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.bookingId) || string.IsNullOrEmpty(req.claimType) || string.IsNullOrEmpty(req.reason))
            {
                return BadRequest(new { error = "Invalid claim details." });
            }

            var userId = GetUserId();

            if (!Guid.TryParse(req.bookingId, out var bookingGuid))
            {
                return BadRequest(new { error = "Invalid booking ID format." });
            }

            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingGuid);
            if (booking == null)
            {
                return NotFound(new { error = "Booking not found." });
            }

            // Only the customer on the booking may claim against it. Without
            // this any signed-in user could raise a claim on somebody else's
            // booking and flip its guarantee status.
            if (booking.UserId != userId)
            {
                return NotFound(new { error = "Booking not found." });
            }

            // Calculate potential refund / compensation
            decimal? refundAmount = booking.TotalAmount;
            decimal? compAmount = req.claimType.ToLower() == "no_show" ? 10000m : 0m;

            var claim = new GuaranteeClaim
            {
                Id = Guid.NewGuid(),
                BookingId = bookingGuid,
                CustomerId = userId,
                VendorId = booking.VendorId,
                ClaimType = req.claimType,
                Reason = req.reason,
                EvidenceJson = JsonSerializer.Serialize(req.evidence ?? new List<string>()),
                Status = "submitted",
                RefundAmount = refundAmount,
                CompensationAmount = compAmount,
                SubmittedAt = DateTime.UtcNow
            };

            _db.GuaranteeClaims.Add(claim);

            // Update booking status if needed
            booking.GuaranteeStatus = "claimed";
            _db.Bookings.Update(booking);

            // One save, so a claim and the flag on its booking cannot disagree.
            await _db.SaveChangesAsync();

            return Ok(new { success = true, data = MapClaim(claim) });
        }

        [Authorize(Policy = "User")]
        [HttpGet("claims")]
        public async Task<IActionResult> GetClaims()
        {
            var userId = GetUserId();

            // A vendor account is not the vendor row the booking points at, so
            // their own claims are found through it.
            var vendorId = await _db.Vendors
                .Where(v => v.UserId == userId)
                .Select(v => (Guid?)v.Id)
                .FirstOrDefaultAsync();

            var claims = await _db.GuaranteeClaims
                .Where(c => c.CustomerId == userId || (vendorId != null && c.VendorId == vendorId))
                .OrderByDescending(c => c.SubmittedAt)
                .ToListAsync();

            return Ok(new { success = true, data = claims.Select(MapClaim).ToList() });
        }

        [Authorize(Policy = "User")]
        [HttpGet("claim/{id}")]
        public async Task<IActionResult> GetClaimById(string id)
        {
            if (!Guid.TryParse(id, out var claimId))
            {
                return NotFound(new { error = "Claim not found." });
            }

            var claim = await _db.GuaranteeClaims.FirstOrDefaultAsync(c => c.Id == claimId);
            if (claim == null)
            {
                return NotFound(new { error = "Claim not found." });
            }

            var userId = GetUserId();
            var vendorId = await _db.Vendors
                .Where(v => v.UserId == userId)
                .Select(v => (Guid?)v.Id)
                .FirstOrDefaultAsync();

            if (claim.CustomerId != userId && claim.VendorId != vendorId)
            {
                // Same answer as a claim that does not exist, so the endpoint
                // cannot be used to discover claim ids.
                return NotFound(new { error = "Claim not found." });
            }

            return Ok(new { success = true, data = MapClaim(claim) });
        }

        /// <summary>Turns a stored claim into the shape the apps read.</summary>
        private static GuaranteeClaimResponse MapClaim(GuaranteeClaim claim) => new()
        {
            id = claim.Id.ToString(),
            bookingId = claim.BookingId.ToString(),
            customerId = claim.CustomerId.ToString(),
            vendorId = claim.VendorId.ToString(),
            claimType = claim.ClaimType,
            reason = claim.Reason,
            evidence = ParseEvidence(claim.EvidenceJson),
            status = claim.Status,
            refundAmount = claim.RefundAmount,
            compensationAmount = claim.CompensationAmount,
            submittedAt = claim.SubmittedAt.ToString("o"),
            resolvedAt = claim.ResolvedAt?.ToString("o"),
            resolution = claim.Resolution
        };

        /// <summary>Evidence URLs, or none at all if the column will not parse.</summary>
        private static List<string> ParseEvidence(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }
    }
}
