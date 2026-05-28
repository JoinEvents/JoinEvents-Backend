using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Application.Tiers
{
    public class TierService : ITierService
    {
        private readonly EventEaseDbContext _db;

        public TierService(EventEaseDbContext db)
        {
            _db = db;
        }

        // ── READ ──────────────────────────────────────────────────────────────

        public async Task<List<Tier>> GetAllAsync()
        {
            return await _db.Tiers
                .Include(t => t.Category)
                .Include(t => t.PriceRanges)
                .OrderBy(t => t.Category.Name)
                .ThenBy(t => t.Name)
                .ToListAsync();
        }

        public async Task<Tier?> GetByIdAsync(Guid id)
        {
            return await _db.Tiers
                .Include(t => t.Category)
                .Include(t => t.PriceRanges)
                .FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task<List<Tier>> GetByCategoryIdAsync(Guid categoryId)
        {
            return await _db.Tiers
                .Include(t => t.Category)
                .Include(t => t.PriceRanges)
                .Where(t => t.CategoryId == categoryId)
                .OrderBy(t => t.Name)
                .ToListAsync();
        }

        // ── CREATE ────────────────────────────────────────────────────────────

        public async Task<(Tier? tier, string? error)> CreateAsync(CreateTierDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return (null, "Tier Name cannot be empty.");

            var category = await _db.EventCategories.FindAsync(dto.CategoryId);
            if (category == null)
                return (null, "Associated category not found.");

            // Uniqueness check per category
            string normalizedName = dto.Name.Trim();
            bool exists = await _db.Tiers.AnyAsync(t => t.Name.ToLower() == normalizedName.ToLower() && t.CategoryId == dto.CategoryId);
            if (exists)
                return (null, $"A Tier named '{dto.Name}' already exists under the category '{category.Name}'.");

            var tier = new Tier
            {
                Id = Guid.NewGuid(),
                Name = normalizedName,
                CategoryId = dto.CategoryId,
                Description = dto.Description?.Trim(),
                IsActive = dto.IsActive,
                Icon = dto.Icon?.Trim() ?? "bi-layers",
                Gradient = dto.Gradient?.Trim() ?? "linear-gradient(135deg,#6B7280,#374151)",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (dto.PriceRanges != null)
            {
                foreach (var pr in dto.PriceRanges)
                {
                    if (string.IsNullOrWhiteSpace(pr.ServiceName))
                        continue;

                    tier.PriceRanges.Add(new TierPriceRange
                    {
                        Id = Guid.NewGuid(),
                        TierId = tier.Id,
                        ServiceName = pr.ServiceName.Trim(),
                        MinPrice = pr.MinPrice,
                        MaxPrice = pr.MaxPrice
                    });
                }
            }

            _db.Tiers.Add(tier);
            await _db.SaveChangesAsync();

            // Reload to fetch category information
            var reloaded = await GetByIdAsync(tier.Id);
            return (reloaded, null);
        }

        // ── UPDATE ────────────────────────────────────────────────────────────

        public async Task<(Tier? tier, string? error)> UpdateAsync(Guid id, UpdateTierDto dto)
        {
            var tier = await _db.Tiers
                .Include(t => t.PriceRanges)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tier == null)
                return (null, "Tier not found.");

            if (dto.Name != null)
            {
                string normalizedName = dto.Name.Trim();
                if (string.IsNullOrWhiteSpace(normalizedName))
                    return (null, "Tier Name cannot be empty.");

                // Validate uniqueness of name if changed
                Guid targetCategoryId = dto.CategoryId ?? tier.CategoryId;
                bool exists = await _db.Tiers.AnyAsync(t => 
                    t.Name.ToLower() == normalizedName.ToLower() && 
                    t.CategoryId == targetCategoryId && 
                    t.Id != id);

                if (exists)
                {
                    var catName = (await _db.EventCategories.FindAsync(targetCategoryId))?.Name ?? "selected category";
                    return (null, $"A Tier named '{dto.Name}' already exists under the {catName}.");
                }

                tier.Name = normalizedName;
            }

            if (dto.CategoryId.HasValue && dto.CategoryId.Value != tier.CategoryId)
            {
                var category = await _db.EventCategories.FindAsync(dto.CategoryId.Value);
                if (category == null)
                    return (null, "Associated category not found.");

                tier.CategoryId = dto.CategoryId.Value;
            }

            if (dto.Description != null)
                tier.Description = dto.Description.Trim();

            if (dto.IsActive.HasValue)
                tier.IsActive = dto.IsActive.Value;

            if (dto.Icon != null)
                tier.Icon = dto.Icon.Trim();

            if (dto.Gradient != null)
                tier.Gradient = dto.Gradient.Trim();

            if (dto.PriceRanges != null)
            {
                // 1. Delete existing price ranges immediately
                _db.TierPriceRanges.RemoveRange(tier.PriceRanges);
                await _db.SaveChangesAsync();

                // 2. Add new price ranges explicitly to DbContext to force EntityState.Added
                foreach (var pr in dto.PriceRanges)
                {
                    if (string.IsNullOrWhiteSpace(pr.ServiceName))
                        continue;

                    var newRange = new TierPriceRange
                    {
                        Id = Guid.NewGuid(),
                        TierId = tier.Id,
                        ServiceName = pr.ServiceName.Trim(),
                        MinPrice = pr.MinPrice,
                        MaxPrice = pr.MaxPrice
                    };
                    _db.TierPriceRanges.Add(newRange);
                }
            }

            tier.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var reloaded = await GetByIdAsync(tier.Id);
            return (reloaded, null);
        }

        // ── DELETE ────────────────────────────────────────────────────────────

        public async Task<(bool success, string? error)> DeleteAsync(Guid id)
        {
            var tier = await _db.Tiers.FindAsync(id);
            if (tier == null)
                return (false, "Tier not found.");

            _db.Tiers.Remove(tier);
            await _db.SaveChangesAsync();
            return (true, null);
        }

        // ── TOGGLE ACTIVE ─────────────────────────────────────────────────────

        public async Task<Tier?> ToggleActiveAsync(Guid id, bool isActive)
        {
            var tier = await _db.Tiers.FindAsync(id);
            if (tier == null) return null;

            tier.IsActive = isActive;
            tier.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return await GetByIdAsync(id);
        }
    }
}
