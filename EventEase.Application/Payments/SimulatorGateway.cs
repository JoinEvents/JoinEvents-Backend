using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Payments
{
    public class SimulatorGateway : IPaymentGateway
    {
        public Task<(string providerRef, string checkoutUrl)> InitiateAsync(Guid bookingId, decimal amount, string method)
          => Task.FromResult((Guid.NewGuid().ToString(), $"https://simulator/checkout/{bookingId}"));
        public Task<bool> ConfirmAsync(string providerRef, string status) => Task.FromResult(status == "success");
    }
}
