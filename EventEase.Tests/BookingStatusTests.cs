using EventEase.Core.Constants;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// The status field used to be assigned straight from the request body. These tests pin the
    /// rules that replaced it.
    /// </summary>
    public class BookingStatusTests
    {
        [Theory]
        [InlineData("pending", "Pending")]
        [InlineData("SETTLED", "Settled")]
        [InlineData("  Confirmed  ", "Confirmed")]
        [InlineData("in_progress", "InProgress")]
        [InlineData("advance_paid", "Paid")]
        public void Normalize_AcceptsKnownStatusesCaseInsensitively(string input, string expected)
        {
            Assert.Equal(expected, BookingStatuses.Normalize(input));
        }

        [Theory]
        [InlineData("not-a-status")]
        [InlineData("")]
        [InlineData(null)]
        public void Normalize_RejectsUnknownStatuses(string? input)
        {
            Assert.Null(BookingStatuses.Normalize(input));
        }

        [Theory]
        [InlineData("Paid", "advance_paid")]
        [InlineData("Accepted", "pending")]
        [InlineData("InProgress", "in_progress")]
        [InlineData("Confirmed", "confirmed")]
        [InlineData("Settled", "settled")]
        public void ToClient_UsesTheAppsStatusNames(string status, string expected)
        {
            Assert.Equal(expected, BookingStatuses.ToClient(status));
        }

        [Fact]
        public void PaidAndSettled_AreReservedForThePaymentFlow()
        {
            Assert.Contains(BookingStatuses.Paid, BookingStatuses.PaymentControlled);
            Assert.Contains(BookingStatuses.Settled, BookingStatuses.PaymentControlled);
        }

        [Fact]
        public void CannotJumpFromPendingStraightToCompleted()
        {
            Assert.False(BookingStatuses.CanTransition(BookingStatuses.Pending, BookingStatuses.Completed));
        }

        [Fact]
        public void CancelledIsTerminal()
        {
            Assert.False(BookingStatuses.CanTransition(BookingStatuses.Cancelled, BookingStatuses.Confirmed));
            Assert.False(BookingStatuses.CanTransition(BookingStatuses.Cancelled, BookingStatuses.Pending));
        }

        [Fact]
        public void AllowsTheNormalFulfilmentPath()
        {
            Assert.True(BookingStatuses.CanTransition(BookingStatuses.Paid, BookingStatuses.Confirmed));
            Assert.True(BookingStatuses.CanTransition(BookingStatuses.Confirmed, BookingStatuses.InProgress));
            Assert.True(BookingStatuses.CanTransition(BookingStatuses.InProgress, BookingStatuses.Completed));
        }

        [Fact]
        public void TransitionToSameStatusIsNotAllowed()
        {
            Assert.False(BookingStatuses.CanTransition(BookingStatuses.Confirmed, BookingStatuses.Confirmed));
        }
    }
}
