using System.Linq;
using System.Threading.Tasks;
using EventEase.Application.Tiers;
using Microsoft.AspNetCore.Mvc;

namespace EventEase.Api.Controllers
{
    [ApiController]
    public class TiersController : ControllerBase
    {
        private readonly ITierService _tiers;

        public TiersController(ITierService tiers)
        {
            _tiers = tiers;
        }

        // ── GET /api/v1/tiers ───────────────────────────────────────────────────
        // Exposes active pricing tiers and their configured icons to all users
        [HttpGet("/api/v1/tiers")]
        public async Task<IActionResult> GetAll()
        {
            var list = await _tiers.GetAllAsync();
            
            // Only return active tiers for public (vendor & customer) layouts
            var data = list.Where(t => t.IsActive).Select(t => new
            {
                id = t.Id,
                name = t.Name,
                categoryId = t.CategoryId,
                categoryName = t.Category?.Name ?? string.Empty,
                description = t.Description,
                icon = t.Icon,
                gradient = t.Gradient,
                priceRanges = t.PriceRanges.Select(pr => new
                {
                    serviceName = pr.ServiceName,
                    minPrice = pr.MinPrice,
                    maxPrice = pr.MaxPrice
                }).ToList()
            });

            return Ok(new { success = true, data });
        }
    }
}
