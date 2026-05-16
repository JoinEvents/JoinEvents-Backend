using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Vendors
{
    public interface IFileStorage
    {
        Task<string> SaveAsync(string container, string fileName, Stream content, string contentType);
    }
}
