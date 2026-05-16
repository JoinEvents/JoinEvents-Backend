using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.SupportTicket
{
    public class Dtos
    {
        public record CreateTicketDto(string Subject, string Description);
        public record UpdateTicketDto(string Status);
    }
}
