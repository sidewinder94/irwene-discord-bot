using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;

namespace DiscordBot.Service.Model
{
    public class Guild : TableEntity 
    {
        [Obsolete("Exists only for technical reasons", true)]
        public Guild()
        {
        }

        public Guild(string guildId)
        {
            this.RowKey = guildId;

            //All guilds should stays in the same partitions
            this.PartitionKey = typeof(Guild).Name;
        }

        public string Id
        {
            get => this.RowKey;
        }

        public string Name { get; set; }

        [IgnoreProperty]
        public ICollection<RoleAssignation> RoleAssignations { get; set; }
    }
}
