using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EventEase.Application.SupportTicket.Dtos;

namespace EventEase.Application.SupportTicket
{
    public interface ISupportService
    {
        Task<EventEase.Core.Entities.SupportTicket> CreateAsync(Guid userId, CreateTicketDto dto);
        Task<List<EventEase.Core.Entities.SupportTicket>> GetAllAsync();
        Task<EventEase.Core.Entities.SupportTicket?> UpdatePropertiesAsync(Guid id, string? status, string? priority);
        Task<DashboardStatsDto> GetDashboardStatsAsync();
    }
}
