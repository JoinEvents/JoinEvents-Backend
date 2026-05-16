using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EventEase.Application.SupportTicket.Dtos;

namespace EventEase.Application.SupportTicket
{
    public class SupportService : ISupportService
    {
        private readonly EventEaseDbContext _db;
        public SupportService(EventEaseDbContext db) => _db = db;

        public async Task<SupportTicket> CreateAsync(Guid userId, CreateTicketDto dto)
        {
            var ticket = new SupportTicket
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Subject = dto.Subject,
                Description = dto.Description
            };
            _db.Set<SupportTicket>().Add(ticket);
            await _db.SaveChangesAsync();
            return ticket;
        }

        public Task<List<SupportTicket>> GetAllAsync() =>
            _db.Set<SupportTicket>().OrderByDescending(t => t.CreatedAt).ToListAsync();

        public async Task<SupportTicket?> UpdateStatusAsync(Guid id, string status)
        {
            var ticket = await _db.Set<SupportTicket>().FindAsync(id);
            if (ticket is null) return null;
            ticket.Status = status;
            ticket.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return ticket;
        }
    }
}
