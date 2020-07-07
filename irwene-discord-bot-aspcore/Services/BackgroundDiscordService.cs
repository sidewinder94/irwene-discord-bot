using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiscordBot.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace irwene_discord_bot_aspcore.Services
{
    public class BackgroundDiscordService : BackgroundService
    {
        private IConfiguration _conf;
        public DiscordService Service;

        public BackgroundDiscordService(IConfiguration configuration, DiscordService service)
        {
            this._conf = configuration;
            this.Service = service;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            this.Service.Start();

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            this.Service.Stop();
        }
    }
}
