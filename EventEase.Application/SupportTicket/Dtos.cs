using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.SupportTicket
{
    public class Dtos
    {
        public record CreateTicketDto(string Subject, string Description, string? EventName, string? AttachmentUrl, Guid? BookingId, string? Priority);
        public record UpdateTicketDto(string? Status, string? Priority);
        public record DashboardStatsDto(
            int OpenTickets,
            int ActiveChats,
            int PendingReviews,
            int TodayResolves,
            int UrgentTickets,
            int HighTickets,
            int MediumLowTickets,
            string AvgResolutionTime,
            string SatisfactionScore
        );
    }
}
