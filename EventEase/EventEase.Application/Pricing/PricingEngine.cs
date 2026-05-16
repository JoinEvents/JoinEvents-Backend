using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Pricing
{
    public record CustomizeDto(string Category, List<Guid> ServiceIds, Dictionary<string, string>? Options);
    public record PriceBreakdown(decimal Total, List<(string item, decimal price)> Items);

    public interface IPricingEngine { Task<PriceBreakdown> CalculateAsync(CustomizeDto dto); }
    public class SimplePricingEngine : IPricingEngine
    {
        private readonly EventEaseDbContext _db;
        public SimplePricingEngine(EventEaseDbContext db) => _db = db;
        public async Task<PriceBreakdown> CalculateAsync(CustomizeDto dto)
        {
            var services = await _db.Services.Where(s => dto.ServiceIds.Contains(s.Id)).ToListAsync();
            var items = services.Select(s => (s.Name, s.Price)).ToList();
            var total = items.Sum(i => i.Price);
            // Options-based adjustments could be applied here
            return new PriceBreakdown(total, items);
        }
    }
}
