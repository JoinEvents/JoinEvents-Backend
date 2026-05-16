using System.Linq;
using System.Threading.Tasks;
using EventEase.Application.Categories;
using Microsoft.AspNetCore.Mvc;

namespace EventEase.Api.Controllers
{
    [ApiController]
    public class EventCategoriesController : ControllerBase
    {
        private readonly IEventCategoryService _categories;

        public EventCategoriesController(IEventCategoryService categories)
        {
            _categories = categories;
        }

        [HttpGet("/api/v1/event-categories")]
        public async Task<IActionResult> GetAll()
        {
            var list = await _categories.GetAllAsync();
            
            // Only return active categories for public
            var data = list.Where(c => c.IsActive).Select(c => new
            {
                id              = c.Id,
                name            = c.Name,
                nameHindi       = c.NameHindi,
                categoryKey     = c.CategoryKey,
                icon            = c.Icon,
                gradient        = c.Gradient,
                colorClass      = c.ColorClass,
                startingPrice   = c.StartingPrice,
                description     = c.Description,
                popularServices = c.PopularServices
            });

            return Ok(new { success = true, data });
        }
    }
}
