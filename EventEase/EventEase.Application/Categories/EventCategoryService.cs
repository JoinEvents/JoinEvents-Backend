using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EventEase.Application.Categories
{
    public class EventCategoryService : IEventCategoryService
    {
        private readonly EventEaseDbContext _db;

        public EventCategoryService(EventEaseDbContext db) => _db = db;

        // ── READ ──────────────────────────────────────────────────────────────

        public async Task<List<EventCategory>> GetAllAsync()
        {
            return await _db.EventCategories
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<EventCategory?> GetByIdAsync(Guid id)
        {
            return await _db.EventCategories.FindAsync(id);
        }

        // ── CREATE ────────────────────────────────────────────────────────────

        public async Task<(EventCategory? category, string? error)> CreateAsync(CreateEventCategoryDto dto)
        {
            // Validate categoryKey format
            if (!IsValidSlug(dto.CategoryKey))
                return (null, "categoryKey must be lowercase with underscores only (a-z, 0-9, _).");

            // Check uniqueness of categoryKey
            bool keyExists = await _db.EventCategories
                .AnyAsync(c => c.CategoryKey == dto.CategoryKey);

            if (keyExists)
                return (null, $"categoryKey '{dto.CategoryKey}' already exists. Choose a unique key.");

            var category = new EventCategory
            {
                Id              = Guid.NewGuid(),
                Name            = dto.Name.Trim(),
                NameHindi       = dto.NameHindi?.Trim(),
                CategoryKey     = dto.CategoryKey.Trim(),
                Icon            = dto.Icon.Trim(),
                Gradient        = dto.Gradient,
                ColorClass      = dto.ColorClass,
                StartingPrice   = dto.StartingPrice,
                Description     = dto.Description?.Trim(),
                IsActive        = dto.IsActive ?? true,
                CreatedAt       = DateTime.UtcNow,
                UpdatedAt       = DateTime.UtcNow
            };

            // Store popular services as JSON
            if (dto.PopularServices is { Count: > 0 })
                category.PopularServices = dto.PopularServices;

            _db.EventCategories.Add(category);
            await _db.SaveChangesAsync();

            return (category, null);
        }

        // ── UPDATE ────────────────────────────────────────────────────────────

        public async Task<(EventCategory? category, string? error)> UpdateAsync(Guid id, UpdateEventCategoryDto dto)
        {
            var category = await _db.EventCategories.FindAsync(id);
            if (category is null)
                return (null, "Category not found.");

            // If categoryKey is being changed, validate and check uniqueness
            if (dto.CategoryKey is not null && dto.CategoryKey != category.CategoryKey)
            {
                if (!IsValidSlug(dto.CategoryKey))
                    return (null, "categoryKey must be lowercase with underscores only (a-z, 0-9, _).");

                bool keyExists = await _db.EventCategories
                    .AnyAsync(c => c.CategoryKey == dto.CategoryKey && c.Id != id);

                if (keyExists)
                    return (null, $"categoryKey '{dto.CategoryKey}' already exists.");

                category.CategoryKey = dto.CategoryKey.Trim();
            }

            // Apply partial updates
            if (dto.Name is not null)            category.Name           = dto.Name.Trim();
            if (dto.NameHindi is not null)       category.NameHindi      = dto.NameHindi.Trim();
            if (dto.Icon is not null)            category.Icon           = dto.Icon.Trim();
            if (dto.Gradient is not null)        category.Gradient       = dto.Gradient;
            if (dto.ColorClass is not null)      category.ColorClass     = dto.ColorClass;
            if (dto.StartingPrice.HasValue)      category.StartingPrice  = dto.StartingPrice;
            if (dto.Description is not null)     category.Description    = dto.Description.Trim();
            if (dto.PopularServices is not null) category.PopularServices = dto.PopularServices;
            if (dto.IsActive.HasValue)           category.IsActive       = dto.IsActive.Value;

            category.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return (category, null);
        }

        // ── DELETE ────────────────────────────────────────────────────────────

        public async Task<(bool success, string? error)> DeleteAsync(Guid id)
        {
            var category = await _db.EventCategories.FindAsync(id);
            if (category is null)
                return (false, "Category not found.");

            // Safety check: warn if any packages reference this category key
            bool inUse = await _db.Packages
                .AnyAsync(p => p.Category == category.CategoryKey);

            if (inUse)
                return (false, $"Cannot delete: vendor services are listed under '{category.Name}'. Deactivate it instead.");

            _db.EventCategories.Remove(category);
            await _db.SaveChangesAsync();
            return (true, null);
        }

        // ── TOGGLE ACTIVE ─────────────────────────────────────────────────────

        public async Task<EventCategory?> ToggleActiveAsync(Guid id, bool isActive)
        {
            var category = await _db.EventCategories.FindAsync(id);
            if (category is null) return null;

            category.IsActive  = isActive;
            category.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return category;
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        /// <summary>Validates that a slug is lowercase alphanumeric + underscores only.</summary>
        private static bool IsValidSlug(string slug)
            => !string.IsNullOrWhiteSpace(slug) && Regex.IsMatch(slug, @"^[a-z0-9_]+$");
    }
}
