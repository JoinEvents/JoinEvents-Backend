using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventEase.Core.Entities;

namespace EventEase.Application.Tiers
{
    // ── DTOs ──────────────────────────────────────────────────────────────────

    public class PriceRangeDto
    {
        public string ServiceName { get; set; } = string.Empty;
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
    }

    public class CreateTierDto
    {
        public string Name { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public string Icon { get; set; } = "bi-layers";
        public string Gradient { get; set; } = "linear-gradient(135deg,#6B7280,#374151)";
        public List<PriceRangeDto> PriceRanges { get; set; } = new();
    }

    public class UpdateTierDto
    {
        public string? Name { get; set; }
        public Guid? CategoryId { get; set; }
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
        public string? Icon { get; set; }
        public string? Gradient { get; set; }
        public List<PriceRangeDto>? PriceRanges { get; set; }
    }

    // ── Interface ─────────────────────────────────────────────────────────────

    public interface ITierService
    {
        Task<List<Tier>> GetAllAsync();
        Task<Tier?> GetByIdAsync(Guid id);
        Task<List<Tier>> GetByCategoryIdAsync(Guid categoryId);
        Task<(Tier? tier, string? error)> CreateAsync(CreateTierDto dto);
        Task<(Tier? tier, string? error)> UpdateAsync(Guid id, UpdateTierDto dto);
        Task<(bool success, string? error)> DeleteAsync(Guid id);
        Task<Tier?> ToggleActiveAsync(Guid id, bool isActive);
    }
}
