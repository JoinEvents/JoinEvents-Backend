using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Services
{
     public class Dtos
    {
        public record AddDto (Guid VendorId,string Name, string Description, string Category, string SubCategory, decimal Price,string Availability,string MediaURL, Constants.ServiceStatus Status = Constants.ServiceStatus.Pending);
        public record GetAllDto(Guid VendorId);
        public record CreateRfpDto(string Title, DateTime EventDate, string City, int GuestCount, decimal BudgetMin, decimal BudgetMax, string Requirements, string[] ServicesNeeded);
        public record PlaceBidDto(decimal ProposedAmount, string Description, string[] Deliverables, DateTime ValidUntil);
        public record PackageSearchResponse(string Id, string Name, Guid VendorId, string VendorBusinessName, decimal Price, string City, int MaxGuests, bool VegOnly, string[] Services);
    }
}
