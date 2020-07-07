using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DiscordBot.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using irwene_discord_bot_aspcore.Models;
using Microsoft.Azure.Cosmos.Table;

namespace irwene_discord_bot_aspcore.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        private readonly DiscordService _service;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration, DiscordService service)
        {
            _logger = logger;
            _configuration = configuration;
            _service = service;
        }

        public IActionResult Index()
        {
            return View(_service);
        }

        public ActionResult StartService()
        {
            this._service.Start();

            return RedirectToAction(nameof(Index));
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
