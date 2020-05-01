using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using irwene_discord_bot_aspcore.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace irwene_discord_bot_aspcore.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            return View();
        }


        private async Task<IActionResult> CreateTable()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(_configuration["secret-azure-tables"]);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference("Guild");
            ViewBag.Success = await table.CreateIfNotExistsAsync();
            ViewBag.TableName = table.Name;

            var entity = new GuildEntity(Guid.NewGuid().ToString())
            {
                Name = "Irwene"
            };

            var operation = TableOperation.Insert(entity);

            var result = await table.ExecuteAsync(operation);

            operation = TableOperation.Retrieve<GuildEntity>(entity.PartitionKey, entity.RowKey);

            result = await table.ExecuteAsync(operation);

            return View();
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
