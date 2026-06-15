using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/commission")]
    [Authorize]
    public class CommissionController : ControllerBase
    {
        private readonly EventEaseDbContext _db;

        public CommissionController(EventEaseDbContext db)
        {
            _db = db;
        }

        public class CalculateRequest
        {
            public decimal bookingAmount { get; set; }
            public string eventCategory { get; set; }
            public string? vendorId { get; set; }
        }

        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            var config = new
            {
                defaultRate = 0.10m,
                categoryRates = new Dictionary<string, decimal>
                {
                    { "wedding", 0.08m },
                    { "corporate", 0.12m },
                    { "social", 0.10m }
                },
                minFee = 500m,
                maxFee = 75000m,
                gstOnCommission = 0.18m,
                tdsRate = 0.01m
            };

            return Ok(new { success = true, data = config });
        }

        [HttpPost("calculate")]
        public async Task<IActionResult> Calculate([FromBody] CalculateRequest req)
        {
            if (req == null || req.bookingAmount <= 0)
            {
                return BadRequest(new { error = "Invalid booking amount." });
            }

            decimal rate = 0.10m; // Default
            string category = (req.eventCategory ?? "social").ToLower();

            if (category.Contains("wed"))
            {
                rate = 0.08m;
            }
            else if (category.Contains("corp"))
            {
                rate = 0.12m;
            }

            // Apply subscription discount if vendor exists
            if (!string.IsNullOrEmpty(req.vendorId) && Guid.TryParse(req.vendorId, out var vendorGuid))
            {
                var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.Id == vendorGuid);
                if (vendor != null)
                {
                    if (vendor.SubscriptionTier == "premium")
                    {
                        rate = Math.Max(0.02m, rate - 0.02m); // 2% off
                    }
                    else if (vendor.SubscriptionTier == "pro")
                    {
                        rate = Math.Max(0.02m, rate - 0.01m); // 1% off
                    }
                }
            }

            decimal commissionAmount = Math.Round(req.bookingAmount * rate, 2);
            decimal gstOnComm = Math.Round(commissionAmount * 0.18m, 2);
            decimal tds = Math.Round(req.bookingAmount * 0.01m, 2);
            decimal payout = Math.Round(req.bookingAmount - commissionAmount - tds, 2);
            decimal platformRevenue = Math.Round(commissionAmount - gstOnComm, 2);

            var result = new
            {
                bookingAmount = req.bookingAmount,
                eventCategory = req.eventCategory ?? "social",
                commissionRate = rate,
                commissionAmount = commissionAmount,
                gstOnCommission = gstOnComm,
                tdsDeduction = tds,
                vendorPayout = payout,
                platformRevenue = platformRevenue
            };

            return Ok(new { success = true, data = result });
        }

        [HttpGet("rates")]
        public IActionResult GetRates()
        {
            var rates = new Dictionary<string, decimal>
            {
                { "wedding", 0.08m },
                { "corporate", 0.12m },
                { "social", 0.10m }
            };

            return Ok(new { success = true, data = rates });
        }
    }
}
