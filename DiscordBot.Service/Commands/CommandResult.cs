using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordBot.Service.Commands
{
    class CommandResult : RuntimeResult
    {
        public CommandResult(CommandError? error, string reason) : base(error, reason)
        {
        }

        public static CommandResult FromError(string reason, CommandError error = CommandError.Unsuccessful) =>
        new CommandResult(error, reason);
        public static CommandResult FromSuccess(string reason = null) =>
            new CommandResult(null, reason);
    }
}
