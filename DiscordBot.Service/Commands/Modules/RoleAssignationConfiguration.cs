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
        public TelemetryClient Telemetry { get; set; }

        [Command("bind")]
        [Summary("Command used to bing a role to a game name")]
        public async Task Bind(
            [Summary("Role to attribute")] SocketRole role,
            [Summary("Game name to watch for")][Remainder] string gameName)
        {
            await this.BindInternal(role, gameName, false);
        }

        [Command("bindr")]
        [Summary("Command used to bind a role as soon as the user presence matches the given regexp")]
        public async Task BindRegExp(
            [Summary("Role to attribute")] SocketRole role,
            [Summary("RegExp to match in the game name")][Remainder] string gameRegExp)
        {
            await this.BindInternal(role, gameRegExp, true);
        }

        [Command("unbind")]
        [Summary("Removes all bindings for a given role")]
        public async Task Unbind(SocketRole role, int? order = null)
        {
            this.AuthorizeRoleAdministrator(true);

            var guildsTable = await GetTableAndCreate<Guild>();

            var guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == role.Guild.Id.ToString()).Take(1)
                .AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                //On aurait pu prendre le guild id du before, pas comme si ça allait changer
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

                var rbatch = await bindingsTable.ExecuteBatchAsync(batchDelete);
            }
            else
            {
                var roleBindingsQuery = bindingsTable.CreateQuery<RoleAssignation>().Where(ass =>
                    ass.PartitionKey == role.Guild.Id.ToString() && ass.RoleStorage == (long)role.Id && ass.Order == order.Value).Take(1).AsTableQuery();

                var roleBinding = bindingsTable.ExecuteQuery(roleBindingsQuery).FirstOrDefault();

                var to = TableOperation.Delete(roleBinding);

                await bindingsTable.ExecuteAsync(to);
            }
        }

        [Command("list")]
        [Summary("Lists all bindings, if given a role, lists all bindings fo given role")]
        public async Task List(SocketRole role)
        {
            this.AuthorizeRoleAdministrator(true);

            var guildsTable = await GetTableAndCreate<Guild>();

            TableQuery<Guild> guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == this.Context.Guild.Id.ToString()).Take(1).AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                await this.Context.Channel.SendMessageAsync("Guild (Server) unknown, try adding a binding first");

                return;
            }

            await guild.LoadChildrens(g => g.RoleAssignations);

            var bindings = guild.RoleAssignations.GroupBy(k => k.RoleId);

            if (role != null)
            {
                bindings = bindings.Where(g => g.Key == role.Id);
            }

            foreach(var roleBindings in bindings)
            {
                var discordRole = this.Context.Guild.GetRole(roleBindings.Key);
         
                var stringBuilder = new StringBuilder();

                foreach (var binding in roleBindings)
                {
                    stringBuilder.AppendLine(
                        $"{binding.Order}: {binding.GameName} {(binding.IsRegExp ? "as a RegExp" : "")}");
                }

                var embedBuilder = new EmbedBuilder()
                    .WithTitle(discordRole.Name)
                    .WithColor(discordRole.Color)
                    .WithDescription(stringBuilder.ToString());

                await this.Context.Channel.SendMessageAsync(embed: embedBuilder.Build());
            }
        }

        private bool AuthorizeRoleAdministrator(bool throwOnUnauthorized = false)
        {
            var authorized = this.Context.Guild.GetUser(this.Context.Message.Author.Id).Roles.Any(r => r.Permissions.Administrator || r.Permissions.ManageRoles);

            if (throwOnUnauthorized && !authorized)
            {
                throw new InvalidOperationException("You don't have the required permissions to administer roles");
            }
            else
            {
                return authorized;
            }
        }

        private async Task BindInternal(SocketRole role, string gameIdent, bool isRegExp)
        {
            this.AuthorizeRoleAdministrator(true);

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


    }
}
