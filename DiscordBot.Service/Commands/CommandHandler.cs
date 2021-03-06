﻿using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Service.Model;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace DiscordBot.Service.Commands
{
    public class CommandHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly IServiceProvider _serviceProvider;
        private readonly TelemetryClient _telemetryClient;

        public CommandHandler(DiscordSocketClient client, CommandService commands)
        {
            _commands = commands;
            _client = client;
        }

        public CommandHandler(DiscordSocketClient client, CommandService commands, IServiceProvider serviceProvider, TelemetryClient telemetryClient) : this(client, commands)
        {
            this._serviceProvider = serviceProvider;
            this._telemetryClient = telemetryClient;
        }

        public async Task InstallCommandsAsync()
        {
            // Hook the MessageReceived event into our command handler
            _client.MessageReceived += HandleCommandAsync;

            // Here we discover all of the command modules in the entry 
            // assembly and load them. Starting from Discord.NET 2.0, a
            // service provider is required to be passed into the
            // module registration method to inject the 
            // required dependencies.
            //
            // If you do not use Dependency Injection, pass null.
            // See Dependency Injection guide for more information.
            await _commands.AddModulesAsync(assembly: Assembly.GetAssembly(typeof(CommandHandler)),
                                            services: this._serviceProvider);

            _commands.CommandExecuted += TrackCommandCompletion;
            _commands.CommandExecuted += HandleCommandCompletion;
        }

        private async Task HandleCommandCompletion(Optional<CommandInfo> commandInfo, ICommandContext commandContext, IResult commandResult)
        {
            IEmote emote = new Emoji("✅");
             
            if (!commandResult.IsSuccess)
            {
                emote = this._client.Guilds
                    .SelectMany(x => x.Emotes)
                    .FirstOrDefault(x => x.Name.IndexOf(
                        "ko_red_check", StringComparison.OrdinalIgnoreCase) != -1);
            }

            await commandContext.Message.AddReactionAsync(emote);
        }

        private async Task TrackCommandCompletion(Optional<CommandInfo> commandInfo, ICommandContext commandContext, IResult commandResult)
        {
            await Task.Run(() =>
            {
                var context = commandContext as CustomCommandContext;

                var commandName = commandInfo.IsSpecified ? commandInfo.Value.Name : commandContext.Message.Content;
                var startTime = context?.CommandReceived ?? DateTimeOffset.UtcNow;
                var duration = DateTimeOffset.UtcNow - startTime;

                var request = new RequestTelemetry(commandName, startTime, duration, $"{commandResult.Error}({commandResult.ErrorReason})", commandResult.IsSuccess);

                this._telemetryClient.TrackRequest(request);
            });
        }

        private async Task HandleCommandAsync(SocketMessage messageParam)
        {
            // Don't process the command if it was a system message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            // Determine if the message is a command based on the prefix and make sure no bots trigger commands
            if (!(message.HasCharPrefix('!', ref argPos) ||
                message.HasMentionPrefix(_client.CurrentUser, ref argPos)) ||
                message.Author.IsBot)
                return;

            // Create a WebSocket-based command context based on the message
            var context = new CustomCommandContext(_client, message);

#if !DEBUG
            // We try to lock the channel so that only one Discord Client instance handles the message
            if (!await ResourceLock.AcquireLock(messageParam.Channel.Id.ToString()))
            {
                // If we didn't manage to acquire the lock, we don't do a thing, another client should already be working on it
                this._telemetryClient.TrackTrace($"Lock failed on channel {messageParam.Channel.Id} ({messageParam.Channel.Name})", SeverityLevel.Information);
                return;
            }

#endif
            // Execute the command with the command context we just
            // created, along with the service provider for precondition checks.

            // Keep in mind that result does not indicate a return value
            // rather an object stating if the command executed successfully.
            var result = await _commands.ExecuteAsync(
                context: context,
                argPos: argPos,
                services: this._serviceProvider);

            // Optionally, we may inform the user if the command fails
            // to be executed; however, this may not always be desired,
            // as it may clog up the request queue should a user spam a
            // command.
            // if (!result.IsSuccess)
            // await context.Channel.SendMessageAsync(result.ErrorReason);
        }
    }
}
