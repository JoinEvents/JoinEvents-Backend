using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using static EventEase.Application.Services.Dtos;

namespace EventEase.Application.Services
{
    public interface IPortalsService
    {
        Task<List<PackageSearchResponse>> SearchPackagesAsync(string? city, string? eventTypeId, decimal? priceMin, decimal? priceMax);
        Task<Rfp> CreateRfpAsync(Guid customerId, CreateRfpDto dto);
        Task<Bid> PlaceBidAsync(Guid rfpId, Guid vendorId, PlaceBidDto dto);
        Task<bool> AcceptBidAsync(Guid rfpId, Guid bidId);
        Task<List<object>> GetRfpsByCustomerIdAsync(Guid customerId);
    }
}
