using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EventEase.Application.Checkout.Dtos;

namespace EventEase.Application.Checkout
{
    public interface ICartService
    {
        Task<CartPreview> PreviewAsync(Guid userId, CartRequest req);
    }
}
