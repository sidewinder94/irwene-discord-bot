using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Service.Commands;
using DiscordBot.Service.Model;
using DiscordBot.Service.Enums;
using DiscordBot.Service.Events;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Configuration;

namespace DiscordBot.Service
{
    public class DiscordService
    {
        private readonly IConfiguration _config;
        private DiscordSocketClient _client;
        private CommandHandler _handler;
        private CommandService _commandService;

        public ServiceStatus Status { get; set; }

        public DiscordService(IConfiguration configuration)
        {
            this._config = configuration;
            TableEntityExtensions.Configuration = configuration;
        }

        private async Task StartAsync()
        {
            if (_client == null)
            {
                var discordConfig = new DiscordSocketConfig
                {
                    GuildSubscriptions = true
                };

                this._client = new DiscordSocketClient(discordConfig);

                var commandServiceConfig = new CommandServiceConfig
                {
                };

                this._commandService = new CommandService(commandServiceConfig);

                this._handler = new CommandHandler(this._client, this._commandService);

                this._client.GuildMemberUpdated += ClientOnGuildMemberUpdated;
            }

            if (_client.LoginState != LoginState.LoggedIn)
            {
                await this._client.LoginAsync(TokenType.Bot, token: _config["discord-bot-token"]);
            }

            await this._client.StartAsync();
        }

        private Task ClientOnGuildMemberUpdated(SocketGuildUser before, SocketGuildUser after)
        {
            new GuildMemberEvents().Updated(before, after);

            return Task.CompletedTask;
        }

        public void Start()
        {
            this.StartAsync().Wait();
            
            this.Status = ServiceStatus.Started;
        }

        private async Task StopAsync()
        {
            await _client.StopAsync();
        }

        public void Stop()
        {
            this.StopAsync().Wait();

            this.Status = ServiceStatus.Stopped;
        }

    }
}
