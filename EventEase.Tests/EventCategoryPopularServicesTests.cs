using EventEase.Core.Entities;
using Xunit;

namespace EventEase.Tests
{
    /// <summary>
    /// PopularServices used to deserialize straight out of the getter, so a single row whose
    /// column did not hold a JSON array threw while the public category list was being
    /// projected — and the whole endpoint answered 500 for every caller. These tests pin the
    /// rule that replaced it: bad data costs that row its tags, nothing more.
    /// </summary>
    public class EventCategoryPopularServicesTests
    {
        [Fact]
        public void Reads_a_json_array()
        {
            var category = new EventCategory { PopularServicesJson = """["Venue","Catering"]""" };

            Assert.Equal(new[] { "Venue", "Catering" }, category.PopularServices);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("[")]
        [InlineData("[not json]")]
        [InlineData("""{"not":"an array"}""")]
        public void Yields_an_empty_list_rather_than_throwing_on_unreadable_data(string stored)
        {
            var category = new EventCategory { PopularServicesJson = stored };

            Assert.Empty(category.PopularServices);
        }

        [Fact]
        public void Reads_a_legacy_comma_separated_value_as_the_list_it_plainly_is()
        {
            var category = new EventCategory { PopularServicesJson = "Venue, Catering ,Decoration" };

            Assert.Equal(new[] { "Venue", "Catering", "Decoration" }, category.PopularServices);
        }

        [Fact]
        public void Round_trips_through_the_setter_as_json()
        {
            var category = new EventCategory
            {
                PopularServices = new List<string> { "Venue", "Catering" }
            };

            Assert.Equal("""["Venue","Catering"]""", category.PopularServicesJson);
            Assert.Equal(new[] { "Venue", "Catering" }, category.PopularServices);
        }
    }
}
