using System;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Application.Tiers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Authorize(Policy = "Admin")]
    public class AdminTiersController : ControllerBase
    {
        private readonly ITierService _tiers;

        public AdminTiersController(ITierService tiers)
        {
            _tiers = tiers;
        }

        public class ToggleActiveDto
        {
            public bool IsActive { get; set; }
        }

        // ── GET /api/v1/admin/tiers ───────────────────────────────────────────

        [HttpGet("/api/v1/admin/tiers")]
        public async Task<IActionResult> GetAll()
        {
            var list = await _tiers.GetAllAsync();
            var data = list.Select(t => MapToResponse(t));
            return Ok(new { success = true, data });
        }

        // ── GET /api/v1/admin/tiers/:id ───────────────────────────────────────

        [HttpGet("/api/v1/admin/tiers/{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var tier = await _tiers.GetByIdAsync(id);
            if (tier is null)
                return NotFound(new { success = false, data = (object?)null, message = "Tier not found." });

            return Ok(new { success = true, data = MapToResponse(tier) });
        }

        // ── GET /api/v1/admin/tiers/by-category/:categoryId ───────────────────

        [HttpGet("/api/v1/admin/tiers/by-category/{categoryId:guid}")]
        public async Task<IActionResult> GetByCategoryId(Guid categoryId)
        {
            var list = await _tiers.GetByCategoryIdAsync(categoryId);
            var data = list.Select(t => MapToResponse(t));
            return Ok(new { success = true, data });
        }

        // ── POST /api/v1/admin/tiers ──────────────────────────────────────────

        [HttpPost("/api/v1/admin/tiers")]
        public async Task<IActionResult> Create([FromBody] CreateTierDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, data = (object?)null, message = "Tier Name is required." });

            if (dto.CategoryId == Guid.Empty)
                return BadRequest(new { success = false, data = (object?)null, message = "CategoryId is required." });

            var (tier, error) = await _tiers.CreateAsync(dto);

            if (error is not null)
                return Conflict(new { success = false, data = (object?)null, message = error });

            return StatusCode(201, new { success = true, data = MapToResponse(tier!), message = "Tier created successfully." });
        }

        // ── PUT /api/v1/admin/tiers/:id ───────────────────────────────────────

        [HttpPut("/api/v1/admin/tiers/{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTierDto dto)
        {
            var (tier, error) = await _tiers.UpdateAsync(id, dto);

            if (error == "Tier not found.")
                return NotFound(new { success = false, data = (object?)null, message = error });

            if (error is not null)
                return Conflict(new { success = false, data = (object?)null, message = error });

            return Ok(new { success = true, data = MapToResponse(tier!), message = "Tier updated successfully." });
        }

        // ── DELETE /api/v1/admin/tiers/:id ────────────────────────────────────

        [HttpDelete("/api/v1/admin/tiers/{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var (success, error) = await _tiers.DeleteAsync(id);

            if (error == "Tier not found.")
                return NotFound(new { success = false, data = (object?)null, message = error });

            if (!success)
                return Conflict(new { success = false, data = (object?)null, message = error });

            return Ok(new { success = true, data = (object?)null, message = "Tier deleted successfully." });
        }

        // ── PATCH /api/v1/admin/tiers/:id/toggle-active ───────────────────────

        [HttpPatch("/api/v1/admin/tiers/{id:guid}/toggle-active")]
        public async Task<IActionResult> ToggleActive(Guid id, [FromBody] ToggleActiveDto dto)
        {
            var tier = await _tiers.ToggleActiveAsync(id, dto.IsActive);
            if (tier is null)
                return NotFound(new { success = false, data = (object?)null, message = "Tier not found." });

            return Ok(new
            {
                success = true,
                data = MapToResponse(tier),
                message = tier.IsActive ? "Tier activated." : "Tier deactivated."
            });
        }

        // ── MAPPER ────────────────────────────────────────────────────────────

        private static object MapToResponse(EventEase.Core.Entities.Tier t) => new
        {
            id = t.Id,
            name = t.Name,
            categoryId = t.CategoryId,
            categoryName = t.Category?.Name ?? string.Empty,
            categoryGradient = t.Category?.Gradient ?? string.Empty,
            description = t.Description,
            isActive = t.IsActive,
            icon = t.Icon,
            gradient = t.Gradient,
            priceRanges = t.PriceRanges.Select(pr => new
            {
                serviceName = pr.ServiceName,
                minPrice = pr.MinPrice,
                maxPrice = pr.MaxPrice
            }).ToList(),
            createdAt = t.CreatedAt,
            updatedAt = t.UpdatedAt
        };
    }
}
