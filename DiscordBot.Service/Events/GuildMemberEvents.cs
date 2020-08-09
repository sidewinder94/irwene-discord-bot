using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DiscordBot.Service.Model;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Cosmos.Table.Queryable;
using static DiscordBot.Service.Model.TableEntityExtensions;

namespace DiscordBot.Service.Events
{
    public class GuildMemberEvents
    {
        public async void Updated(SocketGuildUser before, SocketGuildUser after)
        {
            if(before?.Activity?.Name == after?.Activity?.Name)
            {
                //We don't want to overload the bot by trying to do things when the activity name didn't change (ie: status change, match details change in the activity, ...)
                return;
            }


            var guildsTable = await GetTableAndCreate<Guild>();

            var guildQ = guildsTable.CreateQuery<Guild>().Where(g => g.RowKey == after.Guild.Id.ToString()).Take(1)
                .AsTableQuery();

            var guild = guildsTable.ExecuteQuery(guildQ).FirstOrDefault();

            if (guild == null)
            {
                //On aurait pu prendre le guild id du before, pas comme si ça allait changer
                guild = new Guild(after.Guild);

                var tableOp = TableOperation.InsertOrMerge(guild);

                await guildsTable.ExecuteAsync(tableOp);

                //On vient d'ajouter le serveur (guild) à la liste des serveurs connnus, on a plus rien a faire, puisque aucun binding n'existe
                return;
            }

            //On charge les consignes d'assignation
            await guild.LoadChildrens(g => g.RoleAssignations);

            if (!guild.RoleAssignations.Any())
            {
                //Rien a faire, pas d'assignations
                return;
            }

            var orderedAssignations = guild.RoleAssignations.OrderBy(ra => ra.Order).ToList();

            foreach (var orderedAssignation in orderedAssignations)
            {
                try
                {
                    if (orderedAssignation.IsRegExp)
                    {
                        var regexp = new Regex(orderedAssignation.GameName);

                        if (regexp.IsMatch(after.Activity.Name))
                        {
                            var role = after.Guild.GetRole(orderedAssignation.RoleId);

                            await after.AddRoleAsync(role, RequestOptions.Default);

                            return;
                        }
                    }
                    else
                    {
                        if (after.Activity.Name.Contains(orderedAssignation.GameName))
                        {
                            var role = after.Guild.GetRole(orderedAssignation.RoleId);

                            await after.AddRoleAsync(role, RequestOptions.Default);

                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    continue;
                }
            }
        }
    }
}
