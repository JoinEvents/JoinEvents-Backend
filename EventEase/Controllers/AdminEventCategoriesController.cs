using System;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Application.Categories;
using EventEase.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Authorize(Policy = AuthPolicies.Admin)]
    public class AdminEventCategoriesController : ControllerBase
    {
        private readonly IEventCategoryService _categories;

        public AdminEventCategoriesController(IEventCategoryService categories)
        {
            _categories = categories;
        }

        // ── GET /api/v1/admin/event-categories ────────────────────────────────

        [HttpGet("/api/v1/admin/event-categories")]
        public async Task<IActionResult> GetAll()
        {
            var list = await _categories.GetAllAsync();

            var data = list.Select(c => MapToResponse(c));

            return Ok(new { success = true, data });
        }

        // ── GET /api/v1/admin/event-categories/:id ────────────────────────────

        [HttpGet("/api/v1/admin/event-categories/{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var category = await _categories.GetByIdAsync(id);
            if (category is null)
                return NotFound(new { success = false, data = (object?)null, message = "Category not found." });

            return Ok(new { success = true, data = MapToResponse(category) });
        }

        // ── POST /api/v1/admin/event-categories ───────────────────────────────

        [HttpPost("/api/v1/admin/event-categories")]
        public async Task<IActionResult> Create([FromBody] CreateEventCategoryDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, data = (object?)null, message = "Name is required." });

            if (string.IsNullOrWhiteSpace(dto.CategoryKey))
                return BadRequest(new { success = false, data = (object?)null, message = "categoryKey is required." });

            if (string.IsNullOrWhiteSpace(dto.Icon))
                return BadRequest(new { success = false, data = (object?)null, message = "Icon is required." });

            var (category, error) = await _categories.CreateAsync(dto);

            if (error is not null)
                return Conflict(new { success = false, data = (object?)null, message = error });

            return StatusCode(201, new { success = true, data = MapToResponse(category!), message = "Category created successfully." });
        }

        // ── PUT /api/v1/admin/event-categories/:id ────────────────────────────

        [HttpPut("/api/v1/admin/event-categories/{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEventCategoryDto dto)
        {
            var (category, error) = await _categories.UpdateAsync(id, dto);

            if (error == "Category not found.")
                return NotFound(new { success = false, data = (object?)null, message = error });

            if (error is not null)
                return Conflict(new { success = false, data = (object?)null, message = error });

            return Ok(new { success = true, data = MapToResponse(category!), message = "Category updated successfully." });
        }

        // ── DELETE /api/v1/admin/event-categories/:id ─────────────────────────

        [HttpDelete("/api/v1/admin/event-categories/{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var (success, error) = await _categories.DeleteAsync(id);

            if (error == "Category not found.")
                return NotFound(new { success = false, data = (object?)null, message = error });

            if (!success)
                return Conflict(new { success = false, data = (object?)null, message = error });

            return Ok(new { success = true, data = (object?)null, message = "Category deleted successfully." });
        }

        // ── PATCH /api/v1/admin/event-categories/:id/toggle-active ────────────

        [HttpPatch("/api/v1/admin/event-categories/{id:guid}/toggle-active")]
        public async Task<IActionResult> ToggleActive(Guid id, [FromBody] ToggleActiveDto dto)
        {
            var category = await _categories.ToggleActiveAsync(id, dto.IsActive);
            if (category is null)
                return NotFound(new { success = false, data = (object?)null, message = "Category not found." });

            return Ok(new
            {
                success = true,
                data    = MapToResponse(category),
                message = category.IsActive ? "Category activated." : "Category deactivated."
            });
        }

        // ── MAPPER ────────────────────────────────────────────────────────────

        private static object MapToResponse(EventEase.Core.Entities.EventCategory c) => new
        {
            id              = c.Id,
            name            = c.Name,
            nameHindi       = c.NameHindi,
            categoryKey     = c.CategoryKey,
            icon            = c.Icon,
            gradient        = c.Gradient,
            colorClass      = c.ColorClass,
            startingPrice   = c.StartingPrice,
            description     = c.Description,
            popularServices = c.PopularServices,
            isActive        = c.IsActive,
            createdAt       = c.CreatedAt,
            updatedAt       = c.UpdatedAt
        };
    }
}
