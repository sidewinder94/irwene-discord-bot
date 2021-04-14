using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordBot.Service.Commands
{
    class CustomCommandContext : SocketCommandContext
    {
        public DateTimeOffset CommandReceived { get; private set; }

        public CustomCommandContext(DiscordSocketClient client, SocketUserMessage msg) : base(client, msg)
        {
            this.CommandReceived = DateTimeOffset.UtcNow;
        }
    }
}
