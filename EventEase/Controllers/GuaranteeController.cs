using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/guarantee")]
    public class GuaranteeController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        // In-memory store for claims
        private static readonly ConcurrentBag<GuaranteeClaim> _claims = new ConcurrentBag<GuaranteeClaim>();

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

        public class GuaranteeClaim
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

            // Calculate potential refund / compensation
            decimal? refundAmount = booking.TotalAmount;
            decimal? compAmount = req.claimType.ToLower() == "no_show" ? 10000m : 0m;

            var claim = new GuaranteeClaim
            {
                id = Guid.NewGuid().ToString(),
                bookingId = req.bookingId,
                customerId = userId.ToString(),
                vendorId = booking.VendorId.ToString(),
                claimType = req.claimType,
                reason = req.reason,
                evidence = req.evidence ?? new List<string>(),
                status = "submitted",
                refundAmount = refundAmount,
                compensationAmount = compAmount,
                submittedAt = DateTime.UtcNow.ToString("o")
            };

            // Update booking status if needed
            booking.GuaranteeStatus = "claimed";
            _db.Bookings.Update(booking);
            await _db.SaveChangesAsync();

            _claims.Add(claim);

            return Ok(new { success = true, data = claim });
        }

        [Authorize(Policy = "User")]
        [HttpGet("claims")]
        public IActionResult GetClaims()
        {
            var userId = GetUserId().ToString();
            var userClaims = _claims.Where(c => c.customerId == userId || c.vendorId == userId).ToList();

            return Ok(new { success = true, data = userClaims });
        }

        [Authorize(Policy = "User")]
        [HttpGet("claim/{id}")]
        public IActionResult GetClaimById(string id)
        {
            var claim = _claims.FirstOrDefault(c => c.id == id);
            if (claim == null)
            {
                return NotFound(new { error = "Claim not found." });
            }

            var userId = GetUserId().ToString();
            if (claim.customerId != userId && claim.vendorId != userId)
            {
                return Unauthorized(new { error = "Unauthorized access to claim." });
            }

            return Ok(new { success = true, data = claim });
        }
    }
}
