using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Payments
{
    public interface IPaymentGateway
    {
        Task<(string providerRef, string checkoutUrl)> InitiateAsync(Guid bookingId, decimal amount, string method);
        Task<bool> ConfirmAsync(string providerRef, string status);
    }
}
