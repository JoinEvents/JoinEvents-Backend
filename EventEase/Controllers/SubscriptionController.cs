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
            if (requestedTier != "pro" && requestedTier != "premium" && requestedTier != "free")
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

        private object MapSubscription(Core.Entities.Vendor vendor)
        {
            string tier = vendor.SubscriptionTier ?? "free";
            decimal priceMonthly = tier == "premium" ? 2999m : (tier == "pro" ? 999m : 0m);
            decimal priceYearly = tier == "premium" ? 29990m : (tier == "pro" ? 9990m : 0m);
            int maxListings = tier == "premium" ? 999 : (tier == "pro" ? 10 : 3);
            int featured = tier == "premium" ? 5 : (tier == "pro" ? 1 : 0);
            string analytics = tier == "premium" ? "premium" : (tier == "pro" ? "advanced" : "basic");
            decimal discount = tier == "premium" ? 0.02m : (tier == "pro" ? 0.01m : 0.0m);
            string badge = vendor.SubscriptionBadge ?? "none";
            string status = vendor.SubscriptionExpiry.HasValue && vendor.SubscriptionExpiry.Value < DateTime.UtcNow ? "expired" : "active";

            return new
            {
                vendorId = vendor.Id.ToString(),
                tier = tier,
                priceMonthly = priceMonthly,
                priceYearly = priceYearly,
                maxActiveListings = maxListings,
                featuredListings = featured,
                prioritySupport = tier == "premium",
                analyticsAccess = analytics,
                badgeType = badge,
                commissionDiscount = discount,
                startDate = vendor.CreatedAt.ToString("o"),
                renewalDate = vendor.SubscriptionExpiry?.ToString("o") ?? "",
                status = status
            };
        }
    }
}
