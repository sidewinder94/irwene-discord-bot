using System;
using System.Collections.Generic;
using System.Text;
using Discord;
using Microsoft.Azure.Cosmos.Table;

namespace DiscordBot.Service.Model
{
    public class UserAssignableRoles : TableEntity
    {
        public UserAssignableRoles()
        {
        }

        public UserAssignableRoles(Guild guild, IRole targetRole, IRole fromRole)
        {
            this.PartitionKey = guild.RowKey;
            this.RowKey = Guid.NewGuid().ToString();
            this.Guild = guild;
        }

        private IRole targetRole;

        private IRole fromRole;

        public long TargetRoleStorage { get; set; }

        public long FromRoleStorage { get; set; }

        [IgnoreProperty]
        [Parent(parentType: typeof(Guild))]
        public Guild Guild { get; set; }

        [IgnoreProperty]
        public ulong TargetRoleId
        {
            set => TargetRoleStorage = (long)value;
            get => (ulong)TargetRoleStorage;
        }

        [IgnoreProperty]
        public IRole TargetRole
        {
            get => this.targetRole;
            set
            {
                this.targetRole = value;
                this.TargetRoleId = value.Id;
            }
        }

        [IgnoreProperty]
        public ulong FromRoleId
        {
            set => FromRoleStorage = (long)value;
            get => (ulong)FromRoleStorage;
        }

        [IgnoreProperty]
        public IRole FromRole
        {
            get => this.fromRole;
            set
            {
                this.fromRole = value;
                this.FromRoleId = value.Id;
            }
        }
    }
}
