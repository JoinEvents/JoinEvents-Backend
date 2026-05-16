using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections;

namespace EventEase.Api.Controllers
{
    [ApiController]
    [Route("home")]
    public class HomeController : Controller
    {
        [Authorize(Policy = "User")]
        [HttpGet]
        public IEnumerable Index()
        {
            List<string> list = new List<string>();
            list.Add("User1");
            list.Add("User2");
            list.Add("User3");
            list.Add("User4");
            return list;
        }
    }
}
