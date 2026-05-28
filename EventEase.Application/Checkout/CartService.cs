using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EventEase.Application.Checkout.Dtos;

namespace EventEase.Application.Checkout
{
    public class CartService : ICartService
    {
        private readonly EventEaseDbContext _db;
        public CartService(EventEaseDbContext db) => _db = db;
        public async Task<CartPreview> PreviewAsync(Guid userId, CartRequest req)
        {
            var ids = req.Items.Select(i => i.ServiceId).ToList();
            var svcs = await _db.Services.Where(s => ids.Contains(s.Id)).ToListAsync();
            var lines = new List<(string, int, decimal)>();
            foreach (var item in req.Items)
            {
                var svc = svcs.First(s => s.Id == item.ServiceId);
                lines.Add((svc.Name, item.Qty, svc.Price * item.Qty));
            }
            return new CartPreview(lines.Sum(l => l.Item3), lines);
        }
    }
}
