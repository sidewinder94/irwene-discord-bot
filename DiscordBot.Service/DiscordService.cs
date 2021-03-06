﻿using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Service.Commands;
using DiscordBot.Service.Model;
using DiscordBot.Service.Enums;
using DiscordBot.Service.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace DiscordBot.Service
{
    public class DiscordService
    {
        internal static IConfiguration Config { get; private set; }
        internal static IServiceProvider ServiceProvider { get; private set; }
        private DiscordSocketClient _client;
        private CommandHandler _handler;
        private CommandService _commandService;
        private readonly ILogger<DiscordSocketClient> _clientLogger;
        private readonly ILogger<CommandService> _commandLogger;
        private readonly TelemetryClient _telemetry;
        

        public ServiceStatus Status { get; set; }

        public DiscordService(IConfiguration configuration, ILogger<DiscordSocketClient> clientLogger, ILogger<CommandService> commandLogger, TelemetryClient telemetry, IServiceProvider serviceProvider)
        {
            Config = configuration;
            ServiceProvider = serviceProvider;
            TableEntityExtensions.Configuration = configuration;
            TableEntityExtensions.TelemetryClient = telemetry;
            this._clientLogger = clientLogger;
            this._commandLogger = commandLogger;
            this._telemetry = telemetry;
            
        }

        private async Task StartAsync()
        {
            this._telemetry.TrackEvent("Bot start requested");

            if (_client == null)
            {
                this._telemetry.TrackEvent("Client is null ; First start");

                var discordConfig = new DiscordSocketConfig
                {
                    GuildSubscriptions = true
                };

                this._client = new DiscordSocketClient(discordConfig);

                var commandServiceConfig = new CommandServiceConfig
                {
                };

                this._commandService = new CommandService(commandServiceConfig);

                this._commandService.Log += CommandLog;

                this._handler = new CommandHandler(this._client, this._commandService, ServiceProvider, this._telemetry);

                await this._handler.InstallCommandsAsync();

                this._client.GuildMemberUpdated += ClientOnGuildMemberUpdated;

                this._client.Log += ClientLog;
            }

            if (_client.LoginState != LoginState.LoggedIn)
            {
                this._telemetry.TrackTrace("Logging in", SeverityLevel.Verbose);
                await this._client.LoginAsync(TokenType.Bot, token: Config["discord-bot-token"]);
            }

            await this._client.StartAsync();
        }

        private Task ClientLog(LogMessage logMessage)
        {
            return this.Log(logMessage, this._clientLogger);
        }

        private Task CommandLog(LogMessage logMessage)
        {
            return this.Log(logMessage, this._commandLogger);
        }

        private async Task Log(LogMessage logMessage, ILogger logger)
        {
            await Task.Run(() =>
            {
                LogLevel level;

                #region mapping loglevels
                switch (logMessage.Severity)
                {
                    case LogSeverity.Critical:
                        level = LogLevel.Critical;
                        break;
                    case LogSeverity.Error:
                        level = LogLevel.Error;
                        break;
                    case LogSeverity.Warning:
                        level = LogLevel.Warning;
                        break;
                    case LogSeverity.Info:
                        level = LogLevel.Information;
                        break;
                    case LogSeverity.Verbose:
                        level = LogLevel.Trace;
                        break;
                    case LogSeverity.Debug:
                        level = LogLevel.Debug;
                        break;
                    default:
                        logger.Log(LogLevel.Critical, new ArgumentOutOfRangeException(nameof(logMessage.Severity)), $"Loglevel unkown, message was : {logMessage.Message}");
                        return;
                }
                #endregion

                logger.Log(level, logMessage.Exception, $"Source : {logMessage.Source} ; Message: {logMessage.Message}");
                
            });
        }

        private Task ClientOnGuildMemberUpdated(SocketGuildUser before, SocketGuildUser after)
        {
            Task.Run(() => { new GuildMemberEvents(this._telemetry).Updated(before, after); });

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

        public DiscordSocketClient Client => this._client;

        public void Stop()
        {
            this._telemetry.TrackEvent("Bot stop requested");

            this.StopAsync().Wait();

            this.Status = ServiceStatus.Stopped;
        }

    }
}
