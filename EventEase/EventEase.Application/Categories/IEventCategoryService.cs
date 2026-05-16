using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventEase.Core.Entities;

namespace EventEase.Application.Categories
{
    // ── DTOs ──────────────────────────────────────────────────────────────────

    public class CreateEventCategoryDto
    {
        public string Name { get; set; } = string.Empty;
        public string? NameHindi { get; set; }
        public string CategoryKey { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string? Gradient { get; set; }
        public string? ColorClass { get; set; }
        public decimal? StartingPrice { get; set; }
        public string? Description { get; set; }
        public List<string>? PopularServices { get; set; }
    }

    public class UpdateEventCategoryDto
    {
        public string? Name { get; set; }
        public string? NameHindi { get; set; }
        public string? CategoryKey { get; set; }
        public string? Icon { get; set; }
        public string? Gradient { get; set; }
        public string? ColorClass { get; set; }
        public decimal? StartingPrice { get; set; }
        public string? Description { get; set; }
        public List<string>? PopularServices { get; set; }
    }

    public class ToggleActiveDto
    {
        public bool IsActive { get; set; }
    }

    // ── Interface ─────────────────────────────────────────────────────────────

    public interface IEventCategoryService
    {
        Task<List<EventCategory>> GetAllAsync();
        Task<EventCategory?> GetByIdAsync(Guid id);
        Task<(EventCategory? category, string? error)> CreateAsync(CreateEventCategoryDto dto);
        Task<(EventCategory? category, string? error)> UpdateAsync(Guid id, UpdateEventCategoryDto dto);
        Task<(bool success, string? error)> DeleteAsync(Guid id);
        Task<EventCategory?> ToggleActiveAsync(Guid id, bool isActive);
    }
}
