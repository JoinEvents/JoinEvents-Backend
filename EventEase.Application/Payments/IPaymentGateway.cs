using System;
using System.Threading.Tasks;

namespace EventEase.Application.Payments
{
    public interface IPaymentGateway
    {
        /// <summary>Starts a payment and returns the provider's reference plus a checkout URL.</summary>
        Task<(string providerRef, string checkoutUrl)> InitiateAsync(Guid bookingId, decimal amount, string method);

        /// <summary>
        /// Asks the provider whether this payment actually succeeded.
        ///
        /// This replaces the previous ConfirmAsync(providerRef, status), which simply echoed back
        /// a status supplied by the caller — letting a client declare its own payment successful.
        /// Implementations must query the provider (or validate a signed callback), never trust
        /// input from the browser.
        /// </summary>
        Task<bool> VerifyPaymentAsync(string providerRef);
    }
}
