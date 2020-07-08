using Discord;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordBot.Service.Model
{
    public class RoleAssignation : TableEntity
    {
        public RoleAssignation()
        {
        }

        public RoleAssignation(Guild guild)
        {
            this.RowKey = Guid.NewGuid().ToString();
            this.PartitionKey = guild.RowKey;
            this.Guild = guild;
        }

        private IRole targetRole;

        public string GameName { get; set; }

        public ulong RoleId { private set; get; }

        public bool IsRegExp { get; set; }

        public uint Order { get; set; }
        
        [IgnoreProperty]
        [Parent(parentType: typeof(Guild))]
        public Guild Guild { get; set; }

        [IgnoreProperty]
        public IRole Role { 
            get => this.targetRole;
            set
            {
                this.targetRole = value;
                this.RoleId = value.Id;
            }
        }
    }
}
