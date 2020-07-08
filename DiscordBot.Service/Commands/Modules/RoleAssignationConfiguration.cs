using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Service.Model;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Cosmos.Table.Queryable;
using static DiscordBot.Service.Model.TableEntityExtensions;

namespace DiscordBot.Service.Commands.Modules
{
    [Group("role")]
    public class RoleAssignationConfiguration : ModuleBase<SocketCommandContext>
    {
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

        [Command("Unbind")]
        [Summary("Removes all bindings for a given role")]
        private async Task Unbind(SocketRole role)
        {
            var guildsTable = await GetTableAndCreate<Guild>();

            var guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == role.Guild.Id.ToString()).Take(1)
                .AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                //On aurait pu prendre le guild id du before, pas comme si ça allait changer
                guild = new Guild(role.Guild.Id);

                var tableOp = TableOperation.Insert(guild);

                await guildsTable.ExecuteAsync(tableOp);

                //On vient d'ajouter le serveur (guild) à la liste des serveurs connnus, on a plus rien a faire, puisque aucun binding n'existe
                return;
            }

            var bindingsTable = await GetTableAndCreate<RoleAssignation>();

            var roleBindingsQuery = bindingsTable.CreateQuery<RoleAssignation>().Where(ass =>
                ass.PartitionKey == role.Guild.Id.ToString() && ass.RoleId == role.Id).AsTableQuery();

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
            }
            while (token != null);

            var rbatch = await bindingsTable.ExecuteBatchAsync(batchDelete);
        }

        private async Task BindInternal(SocketRole role, string gameIdent, bool isRegExp)
        {
            var guildsTable = await GetTableAndCreate<Guild>();

            var guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == role.Guild.Id.ToString()).Take(1)
                .AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                //On aurait pu prendre le guild id du before, pas comme si ça allait changer
                guild = new Guild(role.Guild.Id);

                var tableOp = TableOperation.Insert(guild);

                await guildsTable.ExecuteAsync(tableOp);
            }

            var bindingsTable = await GetTableAndCreate<RoleAssignation>();

            await guild.LoadChildrens(g => g.RoleAssignations);

            uint nextOrder = 0;

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
