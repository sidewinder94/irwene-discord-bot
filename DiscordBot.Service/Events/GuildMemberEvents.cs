using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.WebSocket;
using DiscordBot.Service.Model;
using Microsoft.Azure.Cosmos.Table;
using static DiscordBot.Service.Model.TableEntityExtensions;

namespace DiscordBot.Service.Events
{
    public class GuildMemberEvents
    {
        public async void Updated(SocketGuildUser before, SocketGuildUser after)
        {
            var guildsTable = await GetTableAndCreate<Guild>();

            var guild = guildsTable.CreateQuery<Guild>().FirstOrDefault(g => g.RowKey == after.Guild.Id.ToString());

            if (guild == null)
            {
                //On aurait pu prendre le guild id du before, pas comme si ça allait changer
                guild = new Guild(after.Guild.Id);

                var tableOp = TableOperation.Insert(guild);

                await guildsTable.ExecuteAsync(tableOp);

                //On vient d'ajouter le serveur (guild) à la liste des serveurs connnus, on a plus rien a faire, puisque aucun binding n'existe
                return;
            }

            //On charge les consignes d'assignation
            await guild.LoadChildrens(g => g.RoleAssignations);

            throw new NotImplementedException("Pas de raison d'implémenter ça tant qu'on a pas implémenté les commandes de setup");
        }

    }
}
