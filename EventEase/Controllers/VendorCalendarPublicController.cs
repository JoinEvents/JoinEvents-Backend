using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Application.Vendors;
using Microsoft.AspNetCore.Mvc;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/vendor")]
    public class VendorCalendarPublicController : ControllerBase
    {
        private readonly IVendorCalendarService _calendarService;

        public VendorCalendarPublicController(IVendorCalendarService calendarService)
        {
            _calendarService = calendarService;
        }

        /// <summary>
        /// GET /api/v1/vendor/{vendorId}/calendar/check
        /// Checks if a vendor is available on a specific date.
        /// </summary>
        [HttpGet("{vendorId}/calendar/check")]
        public async Task<IActionResult> CheckAvailability(string vendorId, [FromQuery] string? date)
        {
            if (string.IsNullOrEmpty(date) || !DateTime.TryParse(date, out var parsedDate))
            {
                return BadRequest(new { error = "Invalid or missing date parameter. Expected yyyy-MM-dd." });
            }

            // Handle clean Guid parsing and optional prefix removal
            var cleanVendorId = vendorId.Replace("usr_", "").Replace("v_", "");
            if (!Guid.TryParse(cleanVendorId, out var vendorGuid))
            {
                return BadRequest(new { error = "Invalid vendor ID format." });
            }

            var isAvailable = await _calendarService.CheckAvailabilityAsync(vendorGuid, parsedDate);
            return Ok(new { available = isAvailable });
        }

        /// <summary>
        /// GET /api/v1/vendor/calendar/bulk-availability
        /// Checks availability for multiple vendors on a specific date.
        /// </summary>
        [HttpGet("calendar/bulk-availability")]
        public async Task<IActionResult> CheckBulkAvailability([FromQuery] string? date, [FromQuery] string? vendorIds)
        {
            if (string.IsNullOrEmpty(date) || !DateTime.TryParse(date, out var parsedDate))
            {
                return BadRequest(new { error = "Invalid or missing date parameter. Expected yyyy-MM-dd." });
            }

            if (string.IsNullOrEmpty(vendorIds))
            {
                return Ok(new Dictionary<string, bool>());
            }

            var ids = new List<Guid>();
            foreach (var idStr in vendorIds.Split(','))
            {
                var cleanIdStr = idStr.Trim().Replace("usr_", "").Replace("v_", "");
                if (Guid.TryParse(cleanIdStr, out var g))
                {
                    ids.Add(g);
                }
            }

            var availabilityMap = await _calendarService.CheckBulkAvailabilityAsync(ids, parsedDate);

            // Populate multiple formats (raw Guid, prefixed, etc.) to ensure robust client matching
            var result = new Dictionary<string, bool>();
            foreach (var kvp in availabilityMap)
            {
                var rawIdStr = kvp.Key.ToString();
                var prefixedIdStr = $"usr_{kvp.Key:N}";
                
                result[rawIdStr] = kvp.Value;
                result[prefixedIdStr] = kvp.Value;
                result[$"v_{kvp.Key:N}"] = kvp.Value;
            }

            return Ok(result);
        }
    }
}
