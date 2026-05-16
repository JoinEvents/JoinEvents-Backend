using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.SupportTicket
{
    public interface ISupportService
    {
        Task<SupportTicket> CreateAsync(Guid userId, Dtos.CreateTicketDto dto);
        Task<List<SupportTicket>> GetAllAsync();
        Task<SupportTicket?> UpdateStatusAsync(Guid id, string status);
    }
}
