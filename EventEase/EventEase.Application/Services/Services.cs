using EventEase.Core.Entities;
using EventEase.Infrastructure;
using EventEase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EventEase.Application.Services.Dtos;

namespace EventEase.Application.Services
{
    public class Services : IServices
    {
        private readonly EventEaseDbContext _db;
        public Services(EventEaseDbContext db) { _db = db; }
        // Business logic for managing services would go here
        public async Task<Service> AddService(AddDto dot)
        {
            var service = new Service
            {
                Id = Guid.NewGuid(), VendorId = dot.VendorId,Name = dot.Name, Description = dot.Description, Category = dot.Category,SubCategory = dot.SubCategory,Price = dot.Price, Availability = dot.Availability,MediaURL = dot.MediaURL,
                Status = (int)dot.Status
            };
            _db.Services.Add(service);
            await _db.SaveChangesAsync();
            return service;
        }

        public async Task<IEnumerable<Service>> GetAllService(Guid VendorId)
        {
            return await _db.Services.Where(s => s.VendorId == VendorId).ToListAsync();
        }
    }
}
