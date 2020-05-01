using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace irwene_discord_bot_aspcore.Models
{
    public class GuildEntity : TableEntity 
    {
        public GuildEntity(string guildId)
        {
            this.RowKey = guildId;

            //All guilds should stays in the same partitions
            this.PartitionKey = typeof(GuildEntity).Name;
        }

        public string Id
        {
            get => this.RowKey;
        }

        public string Name { get; set; }
    }
}
