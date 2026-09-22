using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/vendor/subscription")]
    public class SubscriptionController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public SubscriptionController(EventEaseDbContext db)
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

        private async Task<Core.Entities.Vendor> GetOrCreateVendor(Guid userId)
        {
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null)
            {
                vendor = new Core.Entities.Vendor
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    BusinessName = "My Vendor Business",
                    IsValidated = false,
                    CreatedAt = DateTime.UtcNow,
                    SubscriptionTier = "free"
                };
                _db.Vendors.Add(vendor);
                await _db.SaveChangesAsync();
            }
            return vendor;
        }

        public class UpgradeRequest
        {
            public string tier { get; set; }
            public string billingCycle { get; set; }
        }

        [Authorize(Policy = "Vendor")]
        [HttpGet]
        public async Task<IActionResult> GetSubscription()
        {
            var userId = GetUserId();
            var vendor = await GetOrCreateVendor(userId);

            var sub = MapSubscription(vendor);
            return Ok(new { success = true, data = sub });
        }

        /// <summary>
        /// Every plan on offer, so a vendor can see what they are not on.
        /// </summary>
        /// <remarks>
        /// GET /vendor/subscription describes only the plan you already hold.
        /// Without this a client wanting to draw a comparison had to hardcode
        /// the prices, which the mobile app did, leaving two places to update.
        /// </remarks>
        [Authorize(Policy = "Vendor")]
        [HttpGet("tiers")]
        public IActionResult GetTiers()
        {
            return Ok(new { success = true, data = Catalogue });
        }

        [Authorize(Policy = "Vendor")]
        [HttpPost("upgrade")]
        public async Task<IActionResult> Upgrade([FromBody] UpgradeRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.tier))
            {
                return BadRequest(new { error = "Invalid tier." });
            }

            var userId = GetUserId();
            var vendor = await GetOrCreateVendor(userId);

            string requestedTier = req.tier.ToLower();
            if (!Catalogue.Any(t => t.tier == requestedTier))
            {
                return BadRequest(new { error = "Unsupported subscription tier." });
            }

            vendor.SubscriptionTier = requestedTier;
            vendor.SubscriptionBadge = requestedTier == "premium" ? "premium" : (requestedTier == "pro" ? "pro" : "none");
            
            if (requestedTier == "free")
            {
                vendor.SubscriptionExpiry = null;
            }
            else
            {
                vendor.SubscriptionExpiry = req.billingCycle?.ToLower() == "yearly" 
                    ? DateTime.UtcNow.AddYears(1) 
                    : DateTime.UtcNow.AddMonths(1);
            }

            _db.Vendors.Update(vendor);
            await _db.SaveChangesAsync();

            var sub = MapSubscription(vendor);
            return Ok(new { success = true, data = sub });
        }

        [Authorize(Policy = "Vendor")]
        [HttpPost("cancel")]
        public async Task<IActionResult> Cancel()
        {
            var userId = GetUserId();
            var vendor = await GetOrCreateVendor(userId);

            vendor.SubscriptionTier = "free";
            vendor.SubscriptionBadge = "none";
            vendor.SubscriptionExpiry = null;

            _db.Vendors.Update(vendor);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = "Subscription cancelled successfully." });
        }

        [Authorize(Policy = "Vendor")]
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var userId = GetUserId();
            var vendor = await GetOrCreateVendor(userId);

            var history = new List<object>();

            if (vendor.SubscriptionTier == "pro" || vendor.SubscriptionTier == "premium")
            {
                history.Add(new
                {
                    id = "SUB-INV-001",
                    date = DateTime.UtcNow.AddDays(-5).ToString("yyyy-MM-dd"),
                    tier = vendor.SubscriptionTier,
                    amount = vendor.SubscriptionTier == "premium" ? 2999 : 999,
                    status = "paid",
                    paymentMethod = "UPI"
                });
            }

            return Ok(new { success = true, data = history });
        }

        /// <summary>
        /// What each plan costs and grants.
        /// </summary>
        /// <remarks>
        /// Stated once, because it used to be a row of ternaries inside the
        /// mapper and nowhere else — so no client could show a vendor what a
        /// plan they were not on would cost, and the mobile app had to carry
        /// its own copy of these numbers to draw the comparison. Changing a
        /// price here now changes it everywhere.
        /// </remarks>
        private static readonly IReadOnlyList<TierOffer> Catalogue = new[]
        {
            new TierOffer
            {
                tier = "free",
                name = "Free",
                priceMonthly = 0m,
                priceYearly = 0m,
                maxActiveListings = 3,
                featuredListings = 0,
                prioritySupport = false,
                analyticsAccess = "basic",
                commissionDiscount = 0.0m,
                badgeType = "none"
            },
            new TierOffer
            {
                tier = "pro",
                name = "Pro",
                priceMonthly = 999m,
                priceYearly = 9990m,
                maxActiveListings = 10,
                featuredListings = 1,
                prioritySupport = false,
                analyticsAccess = "advanced",
                commissionDiscount = 0.01m,
                badgeType = "pro"
            },
            new TierOffer
            {
                tier = "premium",
                name = "Premium",
                priceMonthly = 2999m,
                priceYearly = 29990m,
                maxActiveListings = 999,
                featuredListings = 5,
                prioritySupport = true,
                analyticsAccess = "premium",
                commissionDiscount = 0.02m,
                badgeType = "premium"
            }
        };

        public class TierOffer
        {
            public string tier { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public decimal priceMonthly { get; set; }
            public decimal priceYearly { get; set; }
            public int maxActiveListings { get; set; }
            public int featuredListings { get; set; }
            public bool prioritySupport { get; set; }
            public string analyticsAccess { get; set; } = string.Empty;
            public decimal commissionDiscount { get; set; }
            public string badgeType { get; set; } = string.Empty;
        }

        private static TierOffer OfferFor(string? tier) =>
            Catalogue.FirstOrDefault(t => t.tier.Equals(tier ?? "free", StringComparison.OrdinalIgnoreCase))
            ?? Catalogue[0];

        private object MapSubscription(Core.Entities.Vendor vendor)
        {
            string tier = vendor.SubscriptionTier ?? "free";
            var offer = OfferFor(tier);
            string badge = vendor.SubscriptionBadge ?? "none";
            string status = vendor.SubscriptionExpiry.HasValue && vendor.SubscriptionExpiry.Value < DateTime.UtcNow ? "expired" : "active";

            return new
            {
                vendorId = vendor.Id.ToString(),
                tier = tier,
                priceMonthly = offer.priceMonthly,
                priceYearly = offer.priceYearly,
                maxActiveListings = offer.maxActiveListings,
                featuredListings = offer.featuredListings,
                prioritySupport = offer.prioritySupport,
                analyticsAccess = offer.analyticsAccess,
                badgeType = badge,
                commissionDiscount = offer.commissionDiscount,
                startDate = vendor.CreatedAt.ToString("o"),
                renewalDate = vendor.SubscriptionExpiry?.ToString("o") ?? "",
                status = status
            };
        }
    }
}
