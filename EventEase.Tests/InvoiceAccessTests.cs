using System.Net;
using EventEase.Core.Constants;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// GET /api/v1/invoices/{id}/download was [Authorize] and nothing more: any
    /// signed-in account could read any booking's invoice by guessing an id,
    /// and what came back was the vendor's settlement statement — platform fee,
    /// TDS and net payout — shown to whoever asked, customers included.
    /// </summary>
    public class InvoiceAccessTests : ApiTestBase
    {
        public InvoiceAccessTests(TestWebApplicationFactory factory) : base(factory) { }

        private static string Url(Guid bookingId) => $"/api/v1/invoices/{bookingId}/download";

        [Fact]
        public async Task A_stranger_is_told_the_booking_does_not_exist()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            var stranger = await SeedUser(AuthRoles.Customer);
            var res = await ClientFor(stranger).GetAsync(Url(booking.Id));

            // 404 rather than 403: a 403 would confirm the id is real, which is
            // all an enumeration attempt needs.
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        }

        [Fact]
        public async Task A_vendor_with_no_part_in_the_booking_is_told_the_same()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            var (otherVendorUser, _) = await SeedVendor("Someone Else Events");
            var res = await ClientFor(otherVendorUser).GetAsync(Url(booking.Id));

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        }

        [Fact]
        public async Task An_unknown_booking_id_is_not_found()
        {
            var customer = await SeedUser(AuthRoles.Customer);

            var res = await ClientFor(customer).GetAsync(Url(Guid.NewGuid()));

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        }

        [Fact]
        public async Task The_customer_gets_a_receipt_without_any_payout_figures()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            var res = await ClientFor(customer).GetAsync(Url(booking.Id));
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadAsStringAsync();

            Assert.Contains("JOINEVENTS RECEIPT", body);
            Assert.Contains("Total:", body);
            Assert.Contains("Advance paid:", body);

            // What the customer must not learn: the platform's cut and the
            // vendor's net.
            Assert.DoesNotContain("Platform Fee", body);
            Assert.DoesNotContain("TDS", body);
            Assert.DoesNotContain("Net Vendor Payout", body);
        }

        [Fact]
        public async Task The_customers_receipt_adds_up_to_what_was_charged()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            // 100,000 + 18% GST, with a 30,000 advance already paid.
            var booking = await SeedBooking(customer.Id, vendor.Id, b =>
            {
                b.TotalAmount = 118000m;
                b.AdvanceAmount = 30000m;
                b.DamageCharges = 0m;
            });

            var body = await (await ClientFor(customer).GetAsync(Url(booking.Id))).Content.ReadAsStringAsync();

            Assert.Contains("Package & services: INR 100,000.00", body);
            Assert.Contains("GST (18%):          INR 18,000.00", body);
            Assert.Contains("Total:              INR 118,000.00", body);
            Assert.Contains("Balance due:        INR 88,000.00", body);
        }

        [Fact]
        public async Task Damage_charges_are_listed_and_kept_out_of_the_GST_line()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id, b =>
            {
                b.TotalAmount = 123000m; // 118,000 plus a 5,000 damage charge
                b.DamageCharges = 5000m;
                b.AdvanceAmount = 123000m;
            });

            var body = await (await ClientFor(customer).GetAsync(Url(booking.Id))).Content.ReadAsStringAsync();

            Assert.Contains("Damage charges:     INR 5,000.00", body);
            Assert.Contains("GST (18%):          INR 18,000.00", body);
            Assert.DoesNotContain("Balance due:", body); // paid in full
        }

        [Fact]
        public async Task The_vendor_fulfilling_it_gets_the_settlement_statement()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (vendorUser, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            var res = await ClientFor(vendorUser).GetAsync(Url(booking.Id));
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadAsStringAsync();

            Assert.Contains("JOINEVENTS INVOICE", body);
            Assert.Contains("Platform Fee:    INR 11,800.00", body);
            Assert.Contains("Net Vendor Payout: INR 105,020.00", body);
        }

        [Theory]
        [InlineData(AuthRoles.Admin)]
        [InlineData(AuthRoles.Support)]
        public async Task Staff_get_the_settlement_statement(string role)
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            var staff = await SeedUser(role);
            var res = await ClientFor(staff).GetAsync(Url(booking.Id));
            res.EnsureSuccessStatusCode();

            Assert.Contains("Net Vendor Payout", await res.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task The_download_is_named_for_what_it_actually_is()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (vendorUser, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            var asCustomer = await ClientFor(customer).GetAsync(Url(booking.Id));
            var asVendor = await ClientFor(vendorUser).GetAsync(Url(booking.Id));

            Assert.Contains("Receipt-", asCustomer.Content.Headers.ContentDisposition!.FileName);
            Assert.Contains("Invoice-", asVendor.Content.Headers.ContentDisposition!.FileName);
        }

        [Fact]
        public async Task Signing_in_is_still_required()
        {
            var customer = await SeedUser(AuthRoles.Customer);
            var (_, vendor) = await SeedVendor();
            var booking = await SeedBooking(customer.Id, vendor.Id);

            var res = await Factory.CreateClient().GetAsync(Url(booking.Id));

            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
    }
}
