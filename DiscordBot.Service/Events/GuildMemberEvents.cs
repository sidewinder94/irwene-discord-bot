using System;
using System.Linq;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordBot.Service.Model;
using Microsoft.ApplicationInsights;
using static DiscordBot.Service.Model.TableEntityExtensions;

namespace DiscordBot.Service.Events
{
    public class GuildMemberEvents
    {
        private readonly TelemetryClient _telemetry;

        public GuildMemberEvents(TelemetryClient telemetry)
        {
            this._telemetry = telemetry;
        }

        public async void Updated(SocketGuildUser before, SocketGuildUser after)
        {
            if (before?.Activity?.Name == after?.Activity?.Name
                || string.IsNullOrWhiteSpace(after?.Activity?.Name))
            {
                //We don't want to overload the bot by trying to do things when the activity name didn't change (ie: status change, match details change in the activity, ...)
                return;
            }

            // Try to lock the user
            if (!await ResourceLock.AcquireLock($"{after.Id}"))
            {
                // If we can't another client is already handling it
                this._telemetry.TrackEvent($"Lock failed on user {after.Id} ({after.Username}#{after.DiscriminatorValue})");
                return;
            }

            this._telemetry.TrackEvent("Handling a user status update");

            var guild = await SearchTable<Guild>(g => g.RowKey == after.Guild.Id.ToString()).GetOneAsync();

            if (guild == null)
            {
                //On aurait pu prendre le guild id du before, pas comme si ça allait changer
                guild = new Guild(after.Guild);

                await guild.InsertAsync();

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
                        var regexp = new Regex(orderedAssignation.GameName, RegexOptions.IgnoreCase);

                        if (regexp.IsMatch(after.Activity.Name))
                        {
                            var role = after.Guild.GetRole(orderedAssignation.RoleId);

                            await after.AddRoleAsync(role, RequestOptions.Default);

                            return;
                        }
                    }
                    else
                    {
                        if (after.Activity.Name.ToLowerInvariant().Contains(orderedAssignation.GameName.ToLowerInvariant()))
                        {
                            var role = after.Guild.GetRole(orderedAssignation.RoleId);

                            await after.AddRoleAsync(role, RequestOptions.Default);

                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    this._telemetry.TrackException(e);
                    continue;
                }
            }
        }
    }
}
