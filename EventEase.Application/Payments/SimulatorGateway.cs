using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace EventEase.Application.Payments
{
    /// <summary>
    /// Development-only gateway. It records the references it issues and reports them as paid, so
    /// local checkout flows complete without a real provider.
    ///
    /// This is NOT a payment integration. <see cref="ThrowIfProduction"/> is called by the host at
    /// startup so a production deployment cannot silently run on the simulator.
    /// </summary>
    public class SimulatorGateway : IPaymentGateway
    {
        private readonly ConcurrentDictionary<string, bool> _issued = new();

        public Task<(string providerRef, string checkoutUrl)> InitiateAsync(Guid bookingId, decimal amount, string method)
        {
            var reference = Guid.NewGuid().ToString();
            _issued[reference] = true;
            return Task.FromResult((reference, $"https://simulator.invalid/checkout/{bookingId}"));
        }

        /// <summary>Succeeds only for references this instance issued.</summary>
        public Task<bool> VerifyPaymentAsync(string providerRef)
        {
            return Task.FromResult(!string.IsNullOrWhiteSpace(providerRef) && _issued.ContainsKey(providerRef));
        }

        /// <summary>
        /// Guard used at startup: refuses to run the simulator in a production environment.
        /// </summary>
        public static void ThrowIfProduction(bool isProduction)
        {
            if (isProduction)
            {
                throw new InvalidOperationException(
                    "[SECURITY] SimulatorGateway is a development stub and must not handle production payments. " +
                    "Register a real IPaymentGateway implementation, or set Payments:AllowSimulator=true to " +
                    "explicitly accept a non-functional payment flow.");
            }
        }
    }
}
