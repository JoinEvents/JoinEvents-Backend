using EventEase.Application.Pricing;
using EventEase.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("packages")]
    public class PackagesController : ControllerBase
    {
        private readonly EventEaseDbContext _db;
        private readonly IPricingEngine _pricing;
        public PackagesController(EventEaseDbContext db, IPricingEngine pricing) { _db = db; _pricing = pricing; }

        [HttpGet]
        public Task<IActionResult> GetAll() =>
          Task.FromResult<IActionResult>(Ok(_db.Packages.AsNoTracking().ToList()));

        [HttpPost("customize")]
        public async Task<IActionResult> Customize([FromBody] CustomizeDto dto)
        {
            var breakdown = await _pricing.CalculateAsync(dto);
            return Ok(breakdown);
        }
    }
}
