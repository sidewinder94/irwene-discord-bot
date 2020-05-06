using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DiscordBot.Service.Model;
using DiscordBot.Service.Enums;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Configuration;

namespace DiscordBot.Service
{
    public class DiscordService
    {
        private IConfiguration config;

        public ServiceStatus Status { get; set; }

        public DiscordService(IConfiguration configuration)
        {
            this.config = configuration;
            TableEntityExtensions.Configuration = configuration;
        }

        public void Start()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(config["secret-azure-tables"]);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference("Guild");
            var r = table.CreateIfNotExistsAsync().Result;

            var g = new Guild(Guid.NewGuid().ToString())
            {
                Name = "Irwene"
            };
            var operation = TableOperation.InsertOrReplace(g);

            var result = table.ExecuteAsync(operation).Result;

            var secondTable = tableClient.GetTableReference("RoleAssignation");
            secondTable.CreateIfNotExists();

            foreach (var num in Enumerable.Range(1, 1))
            {
                var n = new RoleAssignation(g)
                {
                    GameName = num.ToString(),
                    RowKey = num.ToString()
                };

                var operationd = TableOperation.InsertOrReplace(n);
                secondTable.Execute(operationd);
            }

            g.LoadChildrens(guild => guild.RolesAssignation).Wait();

            Console.WriteLine(g.RolesAssignation);

            this.Status = ServiceStatus.Started;
        }

        public void Stop()
        {
            this.Status = ServiceStatus.Stopped;
        }

    }
}
