﻿using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace irwene_discord_bot_aspcore.Models
{
    public class GuildEntity : TableEntity 
    {
        [Obsolete("Exists only for technical reasons", true)]
        public GuildEntity()
        {
        }

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

        [IgnoreProperty]
        public Collection<string> Dependents { get; set; }
    }
}
