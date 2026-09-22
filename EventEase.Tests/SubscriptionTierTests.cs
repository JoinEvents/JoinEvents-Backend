using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventEase.Core.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// What each plan costs lived as a row of ternaries inside the mapper for
    /// the plan a vendor already held, so nothing could show them what the
    /// others cost and the mobile app carried its own copy of the numbers.
    /// These pin the catalogue and that the plan endpoint agrees with it.
    /// </summary>
    public class SubscriptionTierTests : ApiTestBase
    {
        public SubscriptionTierTests(TestWebApplicationFactory factory) : base(factory) { }

        private async Task<JsonElement> Tiers(HttpClient client)
        {
            var res = await client.GetAsync("/api/v1/vendor/subscription/tiers");
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("data");
        }

        private static JsonElement Tier(JsonElement tiers, string name) =>
            tiers.EnumerateArray().Single(t => t.GetProperty("tier").GetString() == name);

        [Fact]
        public async Task All_three_plans_are_offered()
        {
            var (vendorUser, _) = await SeedVendor();

            var tiers = await Tiers(ClientFor(vendorUser));

            Assert.Equal(
                new[] { "free", "pro", "premium" },
                tiers.EnumerateArray().Select(t => t.GetProperty("tier").GetString()).ToArray());
        }

        [Fact]
        public async Task Each_plan_states_its_price_and_what_it_grants()
        {
            var (vendorUser, _) = await SeedVendor();
            var tiers = await Tiers(ClientFor(vendorUser));

            var free = Tier(tiers, "free");
            Assert.Equal(0m, free.GetProperty("priceMonthly").GetDecimal());
            Assert.Equal(3, free.GetProperty("maxActiveListings").GetInt32());
            Assert.False(free.GetProperty("prioritySupport").GetBoolean());

            var pro = Tier(tiers, "pro");
            Assert.Equal(999m, pro.GetProperty("priceMonthly").GetDecimal());
            Assert.Equal(9990m, pro.GetProperty("priceYearly").GetDecimal());
            Assert.Equal(10, pro.GetProperty("maxActiveListings").GetInt32());
            Assert.Equal(1, pro.GetProperty("featuredListings").GetInt32());
            Assert.Equal("advanced", pro.GetProperty("analyticsAccess").GetString());

            var premium = Tier(tiers, "premium");
            Assert.Equal(2999m, premium.GetProperty("priceMonthly").GetDecimal());
            Assert.Equal(29990m, premium.GetProperty("priceYearly").GetDecimal());
            Assert.Equal(5, premium.GetProperty("featuredListings").GetInt32());
            Assert.True(premium.GetProperty("prioritySupport").GetBoolean());
            Assert.Equal(0.02m, premium.GetProperty("commissionDiscount").GetDecimal());
        }

        [Fact]
        public async Task Paying_yearly_is_cheaper_than_twelve_months()
        {
            var (vendorUser, _) = await SeedVendor();
            var tiers = await Tiers(ClientFor(vendorUser));

            foreach (var tier in tiers.EnumerateArray().Where(t => t.GetProperty("priceMonthly").GetDecimal() > 0))
            {
                var monthly = tier.GetProperty("priceMonthly").GetDecimal();
                var yearly = tier.GetProperty("priceYearly").GetDecimal();
                Assert.True(yearly < monthly * 12, $"{tier.GetProperty("tier").GetString()} yearly is not a saving");
            }
        }

        [Fact]
        public async Task The_plan_a_vendor_holds_is_priced_from_the_same_table()
        {
            var (vendorUser, vendor) = await SeedVendor();
            await WithDb(async db =>
            {
                var row = await db.Vendors.SingleAsync(v => v.Id == vendor.Id);
                row.SubscriptionTier = "premium";
                await db.SaveChangesAsync();
            });

            var client = ClientFor(vendorUser);
            var catalogue = Tier(await Tiers(client), "premium");

            var body = await (await client.GetAsync("/api/v1/vendor/subscription"))
                .Content.ReadFromJsonAsync<JsonElement>();
            var plan = body.GetProperty("data");

            // The bug this guards against is the two drifting apart.
            Assert.Equal(catalogue.GetProperty("priceMonthly").GetDecimal(), plan.GetProperty("priceMonthly").GetDecimal());
            Assert.Equal(catalogue.GetProperty("priceYearly").GetDecimal(), plan.GetProperty("priceYearly").GetDecimal());
            Assert.Equal(catalogue.GetProperty("maxActiveListings").GetInt32(), plan.GetProperty("maxActiveListings").GetInt32());
            Assert.Equal(catalogue.GetProperty("commissionDiscount").GetDecimal(), plan.GetProperty("commissionDiscount").GetDecimal());
        }

        [Fact]
        public async Task A_vendor_on_no_plan_is_treated_as_being_on_the_free_one()
        {
            var (vendorUser, _) = await SeedVendor();

            var body = await (await ClientFor(vendorUser).GetAsync("/api/v1/vendor/subscription"))
                .Content.ReadFromJsonAsync<JsonElement>();
            var plan = body.GetProperty("data");

            Assert.Equal("free", plan.GetProperty("tier").GetString());
            Assert.Equal(0m, plan.GetProperty("priceMonthly").GetDecimal());
            Assert.Equal(3, plan.GetProperty("maxActiveListings").GetInt32());
        }

        [Fact]
        public async Task A_plan_that_is_not_on_offer_cannot_be_bought()
        {
            var (vendorUser, _) = await SeedVendor();

            var res = await ClientFor(vendorUser).PostAsJsonAsync(
                "/api/v1/vendor/subscription/upgrade",
                new { tier = "platinum", billingCycle = "monthly" });

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        [Fact]
        public async Task Every_plan_on_offer_can_be_bought()
        {
            var (vendorUser, _) = await SeedVendor();
            var client = ClientFor(vendorUser);

            foreach (var tier in new[] { "pro", "premium", "free" })
            {
                var res = await client.PostAsJsonAsync(
                    "/api/v1/vendor/subscription/upgrade",
                    new { tier, billingCycle = "monthly" });

                Assert.True(res.IsSuccessStatusCode, $"{tier} was refused");
            }
        }

        [Fact]
        public async Task The_catalogue_is_for_vendors()
        {
            var customer = await SeedUser(AuthRoles.Customer);

            var res = await ClientFor(customer).GetAsync("/api/v1/vendor/subscription/tiers");

            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }
    }
}
