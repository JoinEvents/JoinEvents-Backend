using EventEase.Infrastructure.Data;
using EventEase.Core.Entities;
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

        public async Task<EventEase.Core.Entities.SupportTicket> CreateAsync(Guid userId, CreateTicketDto dto)
        {
            var ticket = new EventEase.Core.Entities.SupportTicket
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Subject = dto.Subject,
                Description = dto.Description,
                EventName = dto.EventName,
                AttachmentUrl = dto.AttachmentUrl,
                BookingId = dto.BookingId,
                Priority = dto.Priority ?? "Medium"
            };
            _db.Set<EventEase.Core.Entities.SupportTicket>().Add(ticket);
            await _db.SaveChangesAsync();
            return ticket;
        }

        public Task<List<EventEase.Core.Entities.SupportTicket>> GetAllAsync() =>
            _db.Set<EventEase.Core.Entities.SupportTicket>().OrderByDescending(t => t.CreatedAt).ToListAsync();

        public async Task<EventEase.Core.Entities.SupportTicket?> UpdatePropertiesAsync(Guid id, string? status, string? priority)
        {
            var ticket = await _db.Set<EventEase.Core.Entities.SupportTicket>().FindAsync(id);
            if (ticket is null) return null;
            if (!string.IsNullOrEmpty(status)) ticket.Status = status;
            if (!string.IsNullOrEmpty(priority)) ticket.Priority = priority;
            ticket.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return ticket;
        }

        public async Task<DashboardStatsDto> GetDashboardStatsAsync()
        {
            var tickets = await _db.Set<EventEase.Core.Entities.SupportTicket>().ToListAsync();
            var openTickets = tickets.Count(t => t.Status.Equals("Open", StringComparison.OrdinalIgnoreCase) || t.Status.Equals("InProgress", StringComparison.OrdinalIgnoreCase));
            var urgentTickets = tickets.Count(t => t.Priority.Equals("Urgent", StringComparison.OrdinalIgnoreCase));
            var highTickets = tickets.Count(t => t.Priority.Equals("High", StringComparison.OrdinalIgnoreCase));
            var mediumLowTickets = tickets.Count(t => t.Priority.Equals("Medium", StringComparison.OrdinalIgnoreCase) || t.Priority.Equals("Low", StringComparison.OrdinalIgnoreCase));
            var todayResolves = tickets.Count(t => t.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase) && t.UpdatedAt?.Date == DateTime.UtcNow.Date);

            // Dummy active chats and pending reviews for now
            var activeChats = 0;
            var pendingReviews = 0;

            return new DashboardStatsDto(
                openTickets,
                activeChats,
                pendingReviews,
                todayResolves,
                urgentTickets,
                highTickets,
                mediumLowTickets,
                "4.2h",
                "4.8"
            );
        }
    }
}
