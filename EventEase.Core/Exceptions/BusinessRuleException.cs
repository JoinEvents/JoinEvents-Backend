using System;

namespace EventEase.Core.Exceptions
{
    /// <summary>
    /// Thrown when a request is well-formed but violates a domain rule (for example, booking a
    /// package that belongs to a different vendor). Controllers translate this to a 400 response;
    /// the message is written to be safe to show to the caller.
    /// </summary>
    public class BusinessRuleException : Exception
    {
        public BusinessRuleException(string message) : base(message) { }
    }
}
