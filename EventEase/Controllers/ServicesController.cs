using Azure.Core;
using EventEase.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using static EventEase.Application.Services.Dtos;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("services")]
    [Authorize(Policy ="Vendor")]
    public class ServicesController : ControllerBase
    {
        private readonly IServices _services;
        public ServicesController(IServices services) { _services = services; }

        //[HttpGet]
        //public async Task<IActionResult> Get([FromQuery] string? category)
        //{
        //    var q = _db.Services.AsQueryable();
        //    if (!string.IsNullOrWhiteSpace(category)) q = q.Where(s => s.Category == category);
        //    return Ok(await q.Take(200).ToListAsync());
        //}

        [HttpPost("add")]
        public async Task<IActionResult> Add([FromBody] AddDto dto)
        {
            var service = await _services.AddService(dto);
            return Ok(service);
        }

        [HttpGet("getAll")]
        public async Task<IActionResult> GetAll(Guid VendorId)
        {
            if (VendorId == null || string.IsNullOrEmpty(VendorId.ToString()))
                return BadRequest(new { message = "VendorId is required" });

            var data = await _services.GetAllService(VendorId);
            return Ok(new { Services = data });
        }
    }
}
