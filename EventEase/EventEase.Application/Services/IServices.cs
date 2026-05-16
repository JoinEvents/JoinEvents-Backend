using EventEase.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EventEase.Application.Services.Dtos;

namespace EventEase.Application.Services
{
    public interface IServices
    {
        public Task<Service> AddService(AddDto addDto);
        public Task<IEnumerable<Service>> GetAllService(Guid VendorId);
    }
}
