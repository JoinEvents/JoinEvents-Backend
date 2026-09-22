using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventEase.Core.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// Booking protection claims used to live in a static collection on the
    /// controller: lost on restart, invisible to every other instance, and
    /// raiseable against anybody's booking. The flag written onto the booking
    /// did survive, so a customer could be left looking at a booking marked
    /// "claimed" with no claim behind it.
    /// </summary>
    public class GuaranteeClaimTests : ApiTestBase
    {
        public GuaranteeClaimTests(TestWebApplicationFactory factory) : base(factory) { }

        private static object Claim(Guid bookingId, string type = "no_show") => new
        {
            bookingId = bookingId.ToString(),
            claimType = type,
            reason = "The vendor never arrived.",
            evidence = new[] { "https://files.example.test/photo-1.jpg" }
        };

        [Fact]
        public async Task A_claim_is_still_there_after_the_request_that_made_it()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            var res = await ClientFor(customer).PostAsJsonAsync("/api/v1/guarantee/claim", Claim(booking.Id));
            res.EnsureSuccessStatusCode();

            var stored = await WithDb(db => db.GuaranteeClaims.SingleAsync(c => c.BookingId == booking.Id));

            Assert.Equal(customer.Id, stored.CustomerId);
            Assert.Equal(vendor.Id, stored.VendorId);
            Assert.Equal("no_show", stored.ClaimType);
            Assert.Equal("submitted", stored.Status);
            Assert.Equal(booking.TotalAmount, stored.RefundAmount);
            Assert.Equal(10000m, stored.CompensationAmount);
        }

        [Fact]
        public async Task The_evidence_comes_back_out_as_it_went_in()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            await ClientFor(customer).PostAsJsonAsync("/api/v1/guarantee/claim", Claim(booking.Id));

            var res = await ClientFor(customer).GetAsync("/api/v1/guarantee/claims");
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            var claim = body.GetProperty("data").EnumerateArray().Single();

            Assert.Equal(
                "https://files.example.test/photo-1.jpg",
                claim.GetProperty("evidence").EnumerateArray().Single().GetString());
        }

        [Fact]
        public async Task A_quality_claim_carries_no_compensation()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            await ClientFor(customer).PostAsJsonAsync("/api/v1/guarantee/claim", Claim(booking.Id, "quality"));

            var stored = await WithDb(db => db.GuaranteeClaims.SingleAsync(c => c.BookingId == booking.Id));
            Assert.Equal(0m, stored.CompensationAmount);
        }

        [Fact]
        public async Task A_stranger_cannot_claim_against_somebody_elses_booking()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            var stranger = await SeedUser(AuthRoles.Customer);
            var res = await ClientFor(stranger).PostAsJsonAsync("/api/v1/guarantee/claim", Claim(booking.Id));

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

            // And nothing was written — neither a claim nor the flag on the
            // booking, which is what used to be left behind.
            Assert.False(await WithDb(db => db.GuaranteeClaims.AnyAsync(c => c.BookingId == booking.Id)));
            var after = await WithDb(db => db.Bookings.SingleAsync(b => b.Id == booking.Id));
            Assert.NotEqual("claimed", after.GuaranteeStatus);
        }

        [Fact]
        public async Task Claiming_marks_the_booking_as_claimed()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            await ClientFor(customer).PostAsJsonAsync("/api/v1/guarantee/claim", Claim(booking.Id));

            var after = await WithDb(db => db.Bookings.SingleAsync(b => b.Id == booking.Id));
            Assert.Equal("claimed", after.GuaranteeStatus);
        }

        [Fact]
        public async Task The_vendor_sees_the_claim_raised_against_them()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (vendorUser, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            await ClientFor(customer).PostAsJsonAsync("/api/v1/guarantee/claim", Claim(booking.Id));

            var body = await (await ClientFor(vendorUser).GetAsync("/api/v1/guarantee/claims"))
                .Content.ReadFromJsonAsync<JsonElement>();

            var claim = body.GetProperty("data").EnumerateArray().Single();
            Assert.Equal(booking.Id.ToString(), claim.GetProperty("bookingId").GetString());
        }

        [Fact]
        public async Task The_list_holds_nothing_belonging_to_anybody_else()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);
            await ClientFor(customer).PostAsJsonAsync("/api/v1/guarantee/claim", Claim(booking.Id));

            var onlooker = await SeedUser(AuthRoles.Customer);
            var body = await (await ClientFor(onlooker).GetAsync("/api/v1/guarantee/claims"))
                .Content.ReadFromJsonAsync<JsonElement>();

            Assert.Empty(body.GetProperty("data").EnumerateArray());
        }

        [Fact]
        public async Task A_claim_is_readable_by_id_by_the_person_who_raised_it()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);
            await ClientFor(customer).PostAsJsonAsync("/api/v1/guarantee/claim", Claim(booking.Id));

            var id = (await WithDb(db => db.GuaranteeClaims.SingleAsync(c => c.BookingId == booking.Id))).Id;

            var res = await ClientFor(customer).GetAsync($"/api/v1/guarantee/claim/{id}");
            res.EnsureSuccessStatusCode();

            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(id.ToString(), body.GetProperty("data").GetProperty("id").GetString());
        }

        [Fact]
        public async Task Reading_a_claim_by_id_is_refused_to_everyone_else()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);
            await ClientFor(customer).PostAsJsonAsync("/api/v1/guarantee/claim", Claim(booking.Id));

            var id = (await WithDb(db => db.GuaranteeClaims.SingleAsync(c => c.BookingId == booking.Id))).Id;

            var onlooker = await SeedUser(AuthRoles.Customer);
            var res = await ClientFor(onlooker).GetAsync($"/api/v1/guarantee/claim/{id}");

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        }

        [Fact]
        public async Task An_id_that_is_not_a_guid_is_simply_not_found()
        {
            var customer = await SeedUser(AuthRoles.Customer);

            var res = await ClientFor(customer).GetAsync("/api/v1/guarantee/claim/not-a-guid");

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        }
    }
}
