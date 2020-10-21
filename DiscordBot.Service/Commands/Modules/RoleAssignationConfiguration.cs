using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Service.Model;
using JetBrains.Annotations;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Cosmos.Table.Queryable;
using static DiscordBot.Service.Model.TableEntityExtensions;

namespace DiscordBot.Service.Commands.Modules
{
    [Group("role")]
    public class RoleAssignationConfiguration : ModuleBase<SocketCommandContext>
    {

        [NotNull] 
        private readonly TelemetryClient _telemetry;

        public RoleAssignationConfiguration(TelemetryClient telemetryClient)
        {
            this._telemetry = telemetryClient;
        }

        [Command("remove")]
        [Summary("Command used to remove a role from the current user")]
        public async Task Remove(
            [Summary("Role to remove, should be one managed by the bot")] SocketRole roleToRemove,
            [Summary("If the bot should remember that the user does not want the role")] bool forever = false)
        {
            var guildsTable = await GetTableAndCreate<Guild>();

            var guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == roleToRemove.Guild.Id.ToString()).Take(1)
                .AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                guild = new Guild(roleToRemove.Guild);

                var tableOp = TableOperation.Insert(guild);

                await guildsTable.ExecuteAsync(tableOp);

                //On vient d'ajouter le serveur (guild) à la liste des serveurs connnus, on a plus rien a faire, puisque aucun binding n'existe
                return;
            }

            var bindingsTable = await GetTableAndCreate<RoleAssignation>();

            var roleBindingsQuery = bindingsTable.CreateQuery<RoleAssignation>().Where(ass =>
                ass.PartitionKey == roleToRemove.Guild.Id.ToString() && ass.RoleStorage == (long)roleToRemove.Id).Take(1).AsTableQuery();

            var roleBinding = bindingsTable.ExecuteQuery(roleBindingsQuery).FirstOrDefault();

            if (roleBinding == null)
            {
                this._telemetry.TrackTrace(
                    $"Bot was asked by user {this.Context.User.Username}#{this.Context.User.Discriminator} to remove the role " +
                    $"{roleToRemove.Name} which is not managed by this bot for the guild {this.Context.Guild.Name} ({this.Context.Guild.Id})");

                return;
            }

            var user = this.Context.Guild.GetUser(this.Context.User.Id);

            if (user == null)
            {
                this._telemetry.TrackTrace(
                    $"User {this.Context.User.Username}#{this.Context.User.Discriminator} cannot be found in the guild {this.Context.Guild.Name} ({this.Context.Guild.Id})");

                return;
            }

            await user.RemoveRoleAsync(roleToRemove, new RequestOptions { AuditLogReason = "Requested to the bot by the user"});
        }

        [RequireUserPermission(GuildPermission.ManageRoles, Group = "Permission")]
        [RequireOwner(Group = "Permission")]
        [Command("bind")]
        [Summary("Command used to bind a role to a game name")]
        public async Task Bind(
            [Summary("Role to attribute")] SocketRole role,
            [Summary("Game name to watch for")][Remainder] string gameName)
        {
            await this.BindInternal(role, gameName, false);
        }

        [RequireUserPermission(GuildPermission.ManageRoles, Group = "Permission")]
        [RequireOwner(Group = "Permission")]
        [Command("bindr")]
        [Summary("Command used to bind a role as soon as the user presence matches the given regexp")]
        public async Task BindRegExp(
            [Summary("Role to attribute")] SocketRole role,
            [Summary("RegExp to match in the game name")][Remainder] string gameRegExp)
        {
            await this.BindInternal(role, gameRegExp, true);
        }

        [RequireUserPermission(GuildPermission.ManageRoles, Group = "Permission")]
        [RequireOwner(Group = "Permission")]
        [Command("unbind")]
        [Summary("Removes all bindings for a given role")]
        public async Task Unbind(SocketRole role, int? order = null)
        {
            var guildsTable = await GetTableAndCreate<Guild>();

            var guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == role.Guild.Id.ToString()).Take(1)
                .AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                guild = new Guild(role.Guild);

                var tableOp = TableOperation.Insert(guild);

                await guildsTable.ExecuteAsync(tableOp);

                //On vient d'ajouter le serveur (guild) à la liste des serveurs connnus, on a plus rien a faire, puisque aucun binding n'existe
                return;
            }

            var bindingsTable = await GetTableAndCreate<RoleAssignation>();

            if (!order.HasValue)
            {
                var roleBindingsQuery = bindingsTable.CreateQuery<RoleAssignation>().Where(ass =>
                    ass.PartitionKey == role.Guild.Id.ToString() && ass.RoleStorage == (long)role.Id).AsTableQuery();

                var batchDelete = new TableBatchOperation();

                TableContinuationToken token = null;
                do
                {
                    var partialResult = await bindingsTable.ExecuteQuerySegmentedAsync(roleBindingsQuery, token);
                    token = partialResult.ContinuationToken;

                    foreach (var result in partialResult)
                    {
                        batchDelete.Delete(result);
                    }
                } while (token != null);

                await bindingsTable.ExecuteBatchAsync(batchDelete);
            }
            else
            {
                var roleBindingsQuery = bindingsTable.CreateQuery<RoleAssignation>().Where(ass =>
                    ass.PartitionKey == role.Guild.Id.ToString() && ass.RoleStorage == (long)role.Id && ass.Order == order.Value).Take(1).AsTableQuery();

                var roleBinding = bindingsTable.ExecuteQuery(roleBindingsQuery).FirstOrDefault();

                var to = TableOperation.Delete(roleBinding);

                await bindingsTable.ExecuteAsync(to);
            }

            await this.ConsolidateOrder(guild);
        }

        [Command("list")]
        [Summary("Lists all bindings, if given a role, lists all bindings fo given role")]
        public async Task List(SocketRole role = null)
        {
            this._telemetry.TrackEvent($"Binding list requested for Guild {this.Context.Guild.Name} ({this.Context.Guild.Id})");

            var guildsTable = await GetTableAndCreate<Guild>();

            TableQuery<Guild> guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == this.Context.Guild.Id.ToString()).Take(1).AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                guild = new Guild(role.Guild);

                var tableOp = TableOperation.Insert(guild);

                await guildsTable.ExecuteAsync(tableOp);
                
                await this.Context.Channel.SendMessageAsync("Guild (Server) unknown, try adding a binding first");

                return;
            }

            await guild.LoadChildrens(g => g.RoleAssignations);

            var bindings = guild.RoleAssignations.GroupBy(k => k.RoleId);

            if (role != null)
            {
                bindings = bindings.Where(g => g.Key == role.Id);
            }

            foreach(var roleBindings in bindings.OrderBy(k => k.Key))
            {
                var discordRole = this.Context.Guild.GetRole(roleBindings.Key);
         
                var stringBuilder = new StringBuilder();

                foreach (var binding in roleBindings)
                {
                    stringBuilder.AppendLine(
                        $"{binding.Order}: {binding.GameName} {(binding.IsRegExp ? "as a RegExp" : "")}");
                }

                var embedBuilder = new EmbedBuilder()
                    .WithTitle($"@{discordRole.Name}")
                    .WithColor(discordRole.Color)
                    .WithDescription(stringBuilder.ToString());

                await this.Context.Channel.SendMessageAsync(embed: embedBuilder.Build());
            }
        }

        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [RequireOwner(Group = "Permission")]
        [Command("consolidate")]
        [Summary("Lists all bindings, if given a role, lists all bindings fo given role")]
        public async Task Consolidate()
        {
            this._telemetry.TrackEvent($"Order consolidation requested for {this.Context.Guild.Name} ({this.Context.Guild.Id})");

            var guildsTable = await GetTableAndCreate<Guild>();

            TableQuery<Guild> guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == this.Context.Guild.Id.ToString()).Take(1).AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                this._telemetry.TrackEvent($"Guild {this.Context.Guild.Name} ({this.Context.Guild.Id}) unknown, doing nothing");

                return;
            }

            await this.ConsolidateOrder(guild);
        }

        [RequireOwner]
        [Command(nameof(Error))]
        public async Task Error()
        {
            var ex = new ArgumentException();

            this._telemetry.TrackException(ex);

            throw ex;
        }

        private async Task BindInternal(SocketRole role, string gameIdent, bool isRegExp)
        {

            var guildsTable = await GetTableAndCreate<Guild>();

            var guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == role.Guild.Id.ToString()).Take(1)
                .AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                guild = new Guild(role.Guild);

                var tableOp = TableOperation.Insert(guild);

                await guildsTable.ExecuteAsync(tableOp);
            }

            var bindingsTable = await GetTableAndCreate<RoleAssignation>();

            await guild.LoadChildrens(g => g.RoleAssignations);

            long nextOrder = 0;

            if (guild.RoleAssignations.Any())
            {
                var lastOrder = guild.RoleAssignations.Max(a => a.Order);

                nextOrder = lastOrder + 1;
            }

            var insert = new RoleAssignation(guild)
            {
                GameName = gameIdent,
                IsRegExp = isRegExp,
                Role = role,
                Order = nextOrder
            };

            var insertOp = TableOperation.Insert(insert);

            await bindingsTable.ExecuteAsync(insertOp);
        }

        private async Task ConsolidateOrder(Guild guild)
        {
            if (guild.RoleAssignations == null || !guild.RoleAssignations.Any())
            {
                await guild.LoadChildrens(g => g.RoleAssignations);
            }

            if (!guild.RoleAssignations.Any())
            {
                this._telemetry.TrackEvent("No roles to reorder, finishing");
                return;
            }

            var lastOrder = 0;

            var batch = new TableBatchOperation();

            foreach (var roleAssignation in guild.RoleAssignations.OrderBy(ra => ra.Order))
            {
                roleAssignation.Order = lastOrder;

                lastOrder += 1;

                batch.Merge(roleAssignation);
            }

            if (batch.Any())
            {
                var bindingsTable = GetTable<RoleAssignation>();

                await bindingsTable.ExecuteBatchAsync(batch);
            }
        }
    }
}
