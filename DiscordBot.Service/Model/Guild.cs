using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using Discord;

namespace DiscordBot.Service.Model
{
    public class Guild : TableEntity 
    {
        [Obsolete("Exists only for technical reasons", true)]
        public Guild()
        {
        }

        public Guild(IGuild guild) : this(guild.Id)
        {
            this.Name = guild.Name;
        }

        public Guild(ulong guildId)
        {
            this.RowKey = guildId.ToString();

            //All guilds should stays in the same partitions
            this.PartitionKey = nameof(Guild);
        }

        public ulong Id
        {
            get => ulong.Parse(this.RowKey);
        }

        public string Name { get; set; }

        [IgnoreProperty]
        public ICollection<RoleAssignation> RoleAssignations { get; set; }

        [IgnoreProperty]
        public ICollection<UserAssignableRoles> AssignableRoles { get; set; }
    }
}
