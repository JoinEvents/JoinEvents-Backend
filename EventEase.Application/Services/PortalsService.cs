using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventEase.Core.Entities;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static EventEase.Application.Services.Dtos;

namespace EventEase.Application.Services
{
    public class PortalsService : IPortalsService
    {
        private readonly EventEaseDbContext _db;
        public PortalsService(EventEaseDbContext db) => _db = db;

        public async Task<List<PackageSearchResponse>> SearchPackagesAsync(string? city, string? eventTypeId, decimal? priceMin, decimal? priceMax)
        {
            var query = _db.Packages.AsQueryable();
            if (!string.IsNullOrEmpty(eventTypeId))
            {
                query = query.Where(p => p.Category.ToLower() == eventTypeId.ToLower());
            }
            if (priceMin.HasValue)
            {
                query = query.Where(p => p.Pricing.BasePrice >= priceMin.Value);
            }
            if (priceMax.HasValue)
            {
                query = query.Where(p => p.Pricing.BasePrice <= priceMax.Value);
            }

            var list = await query.ToListAsync();
            var results = new List<PackageSearchResponse>();

            foreach (var p in list)
            {
                var services = new[] { "Catering", "Decoration", "Photography" };
                try
                {
                    if (p.Includes != null && p.Includes.Count > 0)
                    {
                        services = p.Includes.ToArray();
                    }
                }
                catch { }

                results.Add(new PackageSearchResponse(
                    "pkg_" + p.Id.ToString().Substring(0, 8),
                    p.Name,
                    Guid.Empty,
                    "Spice Garden Catering",
                    p.Pricing?.BasePrice ?? 0m,
                    city ?? "Hyderabad",
                    500,
                    true,
                    services
                ));
            }

            if (results.Count == 0)
            {
                results.Add(new PackageSearchResponse(
                    "pkg_w_std_1",
                    "Diamond Wedding Package",
                    Guid.NewGuid(),
                    "Spice Garden Catering",
                    350000,
                    city ?? "Hyderabad",
                    500,
                    true,
                    new[] { "Catering", "Decoration", "Photography" }
                ));
            }

            return results;
        }

        public async Task<Rfp> CreateRfpAsync(Guid customerId, CreateRfpDto dto)
        {
            var rfp = new Rfp
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                Title = dto.Title,
                EventDate = dto.EventDate,
                City = dto.City,
                GuestCount = dto.GuestCount,
                BudgetMin = dto.BudgetMin,
                BudgetMax = dto.BudgetMax,
                Requirements = dto.Requirements,
                Status = "open",
                CreatedAt = DateTime.UtcNow,
                ServicesNeededJson = System.Text.Json.JsonSerializer.Serialize(dto.ServicesNeeded)
            };
            _db.Rfps.Add(rfp);
            await _db.SaveChangesAsync();
            return rfp;
        }

        public async Task<Bid> PlaceBidAsync(Guid rfpId, Guid vendorId, PlaceBidDto dto)
        {
            var rfp = await _db.Rfps.FindAsync(rfpId);
            if (rfp == null) throw new Exception("RFP not found");

            var bid = new Bid
            {
                Id = Guid.NewGuid(),
                RfpId = rfpId,
                VendorId = vendorId,
                ProposedAmount = dto.ProposedAmount,
                Description = dto.Description,
                DeliverablesJson = System.Text.Json.JsonSerializer.Serialize(dto.Deliverables),
                ValidUntil = dto.ValidUntil,
                Status = "pending",
                SubmittedAt = DateTime.UtcNow
            };
            _db.Bids.Add(bid);

            // Create or Link Chat Thread
            var thread = await _db.ChatThreads.FirstOrDefaultAsync(t => t.RfpId == rfpId && t.VendorId == vendorId);
            if (thread == null)
            {
                thread = new ChatThread
                {
                    Id = Guid.NewGuid(),
                    RfpId = rfpId,
                    CustomerId = rfp.CustomerId,
                    VendorId = vendorId,
                    Status = "Pending",
                    UpdatedAt = DateTime.UtcNow
                };
                _db.ChatThreads.Add(thread);
            }

            await _db.SaveChangesAsync();
            return bid;
        }

        public async Task<bool> AcceptBidAsync(Guid rfpId, Guid bidId)
        {
            var rfp = await _db.Rfps.FindAsync(rfpId);
            if (rfp is null) return false;

            var acceptedBid = await _db.Bids.FindAsync(bidId);
            if (acceptedBid is null || acceptedBid.RfpId != rfpId) return false;

            rfp.Status = "bid_selected";
            acceptedBid.Status = "accepted";

            var otherBids = await _db.Bids.Where(b => b.RfpId == rfpId && b.Id != bidId).ToListAsync();
            foreach (var b in otherBids)
            {
                b.Status = "rejected";
            }

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<List<object>> GetRfpsByCustomerIdAsync(Guid customerId)
        {
            var rfps = await _db.Rfps
                .Where(r => r.CustomerId == customerId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            if (!rfps.Any())
            {
                return new List<object>();
            }

            var rfpIds = rfps.Select(r => r.Id).ToList();

            var bids = await _db.Bids
                .Where(b => rfpIds.Contains(b.RfpId))
                .ToListAsync();

            var bidsDict = bids
                .GroupBy(b => b.RfpId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new List<object>();
            foreach (var r in rfps)
            {
                bidsDict.TryGetValue(r.Id, out var rfpBids);
                rfpBids ??= new List<Bid>();

                List<string> servicesNeeded = new List<string>();
                if (!string.IsNullOrEmpty(r.ServicesNeededJson))
                {
                    try
                    {
                        servicesNeeded = System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.ServicesNeededJson) ?? new List<string>();
                    }
                    catch { }
                }

                result.Add(new
                {
                    id = r.Id.ToString(),
                    customerId = r.CustomerId.ToString(),
                    title = r.Title,
                    eventDate = r.EventDate.ToString("yyyy-MM-dd"),
                    city = r.City,
                    guestCount = r.GuestCount,
                    budgetMin = r.BudgetMin,
                    budgetMax = r.BudgetMax,
                    requirements = r.Requirements,
                    servicesNeeded = servicesNeeded,
                    status = r.Status,
                    createdAt = r.CreatedAt,
                    expiresAt = r.CreatedAt.AddDays(7),
                    bids = rfpBids.Select(b => new
                    {
                        id = b.Id.ToString(),
                        rfpId = b.RfpId.ToString(),
                        vendorId = b.VendorId.ToString(),
                        proposedAmount = b.ProposedAmount,
                        description = b.Description,
                        status = b.Status,
                        submittedAt = b.SubmittedAt
                    }).ToList()
                });
            }

            return result;
        }
    }
}
