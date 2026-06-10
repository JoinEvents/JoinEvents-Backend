using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("api/v1/locations")]
    public class LocationsController : ControllerBase
    {
        [HttpGet("cities")]
        public IActionResult GetSupportedCities()
        {
            var cities = new List<object>
            {
                new { id = "hyderabad", name = "Hyderabad" },
                new { id = "bangalore", name = "Bangalore" },
                new { id = "chennai", name = "Chennai" },
                new { id = "mumbai", name = "Mumbai" },
                new { id = "delhi", name = "Delhi" }
            };

            return Ok(new { success = true, data = cities });
        }
    }
}
