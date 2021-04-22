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
        public async Task<RuntimeResult> Remove(
            [Summary("Role to remove, should be one managed by the bot")] SocketRole roleToRemove,
            [Summary("If the bot should remember that the user does not want the role")] bool forever = false)
        {
            var guild = await SearchTable<Guild>(g => g.RowKey == roleToRemove.Guild.Id.ToString())
                .GetOneAsync();

            if (guild == null)
            {
                guild = new Guild(roleToRemove.Guild);

                await guild.InsertAsync();

                //On vient d'ajouter le serveur (guild) à la liste des serveurs connnus, on a plus rien a faire, puisque aucun binding n'existe
                return CommandResult.FromSuccess("Unknown Guild, nothing to do");
            }

            var roleBinding =
                await SearchTable<RoleAssignation>(ass => ass.PartitionKey == roleToRemove.Guild.Id.ToString()
                                                          && ass.RoleStorage == (long)roleToRemove.Id)
                    .GetOneAsync();

            if (roleBinding == null)
            {
                var error =
                    $"Bot was asked by user {this.Context.User.Username}#{this.Context.User.Discriminator} to remove the role " +
                    $"{roleToRemove.Name} which is not managed by this bot for the guild {this.Context.Guild.Name} ({this.Context.Guild.Id})";
                
                this._telemetry.TrackTrace(error);
                return CommandResult.FromError(error);
            }

            var user = this.Context.Guild.GetUser(this.Context.User.Id);

            if (user == null)
            {
                var error =
                    $"User {this.Context.User.Username}#{this.Context.User.Discriminator} cannot be found in the guild {this.Context.Guild.Name} ({this.Context.Guild.Id})";
                
                this._telemetry.TrackTrace(error);

                return CommandResult.FromError(error);
            }

            await user.RemoveRoleAsync(roleToRemove, new RequestOptions { AuditLogReason = $"Requested to the bot by user {this.Context.User.Username}#{this.Context.User.Discriminator}" });

            return CommandResult.FromSuccess();
        }

        [RequireUserPermission(GuildPermission.ManageRoles, Group = "Permission")]
        [RequireOwner(Group = "Permission")]
        [Command("bind")]
        [Summary("Command used to bind a role to a game name")]
        public async Task<RuntimeResult> Bind(
            [Summary("Role to attribute")] SocketRole role,
            [Summary("Game name to watch for")][Remainder] string gameName)
        {
            await this.BindInternal(role, gameName, false);

            return CommandResult.FromSuccess();
        }

        [RequireUserPermission(GuildPermission.ManageRoles, Group = "Permission")]
        [RequireOwner(Group = "Permission")]
        [Command("bindr")]
        [Summary("Command used to bind a role as soon as the user presence matches the given regexp")]
        public async Task<RuntimeResult> BindRegExp(
            [Summary("Role to attribute")] SocketRole role,
            [Summary("RegExp to match in the game name")][Remainder] string gameRegExp)
        {
            await this.BindInternal(role, gameRegExp, true);

            return CommandResult.FromSuccess();
        }

        [RequireUserPermission(GuildPermission.ManageRoles, Group = "Permission")]
        [RequireOwner(Group = "Permission")]
        [Command("unbind")]
        [Summary("Removes all bindings for a given role")]
        public async Task<RuntimeResult> Unbind(SocketRole role, int? order = null)
        {
            var guild = await SearchTable<Guild>(g => g.RowKey == role.Guild.Id.ToString())
                .GetOneAsync();

            if (guild == null)
            {
                guild = new Guild(role.Guild);

                await guild.InsertAsync();

                //On vient d'ajouter le serveur (guild) à la liste des serveurs connnus, on a plus rien a faire, puisque aucun binding n'existe
                return CommandResult.FromError("New guild, Nothing to do");
            }

            if (!order.HasValue)
            {
                await DeleteBatchAsync<RoleAssignation>(ass =>
                    ass.PartitionKey == role.Guild.Id.ToString() && ass.RoleStorage == (long)role.Id);
            }
            else
            {
                var roleBinding =
                    await SearchTable<RoleAssignation>(ass => ass.PartitionKey == role.Guild.Id.ToString()
                                                              && ass.RoleStorage == (long)role.Id
                                                              && ass.Order == order.Value)
                    .GetOneAsync();

                await roleBinding.DeleteAsync();
            }

            await this.ConsolidateOrder(guild);

            return CommandResult.FromSuccess("New guild, Nothing to do");
        }

        [Command("list")]
        [Summary("Lists all bindings, if given a role, lists all bindings for given role")]
        public async Task List(SocketRole role = null)
        {
            this._telemetry.TrackEvent($"Binding list requested for Guild {this.Context.Guild.Name} ({this.Context.Guild.Id})");

            var guild = await SearchTable<Guild>(g => g.RowKey == this.Context.Guild.Id.ToString())
                .GetOneAsync();

            if (guild == null)
            {
                guild = new Guild(role.Guild);

                await guild.InsertAsync();

                //On vient d'ajouter le serveur (guild) à la liste des serveurs connnus, on a plus rien a faire, puisque aucun binding n'existe
                return;
            }

            await guild.LoadChildrens(g => g.RoleAssignations);

            var bindings = guild.RoleAssignations.GroupBy(k => k.RoleId);

            if (role != null)
            {
                bindings = bindings.Where(g => g.Key == role.Id);
            }

            foreach (var roleBindings in bindings.OrderBy(k => k.Key))
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

            var guild = await SearchTable<Guild>(g => g.RowKey == this.Context.Guild.Id.ToString())
                .GetOneAsync();

            if (guild == null)
            {
                this._telemetry.TrackEvent($"Guild {this.Context.Guild.Name} ({this.Context.Guild.Id}) unknown, doing nothing");

                return;
            }

            await this.ConsolidateOrder(guild);
        }

        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command(nameof(AllowAssign))]
        public async Task<RuntimeResult> AllowAssign(SocketRole fromRole, SocketRole assignableRole)
        {
            var guild = await SearchTable<Guild>(g => g.RowKey == fromRole.Guild.Id.ToString())
                .GetOneAsync();

            if (guild == null)
            {
                guild = new Guild(fromRole.Guild);

                await guild.InsertAsync();
            }

            await guild.LoadChildrens(g => g.AssignableRoles);

            if (guild.AssignableRoles.Any(ar => ar.FromRoleId == fromRole.Id && ar.TargetRoleId == assignableRole.Id))
            {
                var error =
                    $"Allow Assign called with already authorized role assignation on guild {fromRole.Guild.Name}:{fromRole.Guild.Id} for {fromRole.Name}:{fromRole.Id} to assign {assignableRole.Name}:{assignableRole.Id}";

                this._telemetry.TrackEvent(error);
                return CommandResult.FromError(error);
            }

            var newAssignableRole = new UserAssignableRoles(guild, assignableRole, fromRole);
            await newAssignableRole.InsertAsync();

            return CommandResult.FromSuccess();
        }

        [Command(nameof(Assign))]
        public async Task Assign(SocketRole targetRole, SocketGuildUser targetUser)
        {
            var roleAssignations = await SearchTable<UserAssignableRoles>(uar => uar.PartitionKey == targetRole.Guild.Id.ToString() && uar.TargetRoleStorage == (long)targetRole.Id).GetCollectionAsync();

            if(!roleAssignations.Any())
            {
                this._telemetry.TrackEvent($"No existing role assignations for guild {targetRole.Guild.Name}:{targetRole.Guild.Id}");
                return;
            }

            var callerRoles = this.Context.Guild.GetUser(this.Context.User.Id)?.Roles;
            
            if(callerRoles == null)
            {
                this._telemetry.TrackEvent($"Uknown user {targetUser.Nickname}:{targetUser.Id} on guild {targetRole.Guild.Name}:{targetRole.Guild.Id}");
                return;
            }

            if(roleAssignations.Any(ra => callerRoles.Select(cr => cr.Id).Contains(ra.FromRoleId)))
            {
                await targetUser.AddRoleAsync(targetRole, RequestOptions.Default);
                return;
            }

            this._telemetry.TrackEvent($"Role {targetRole.Name}:{targetRole.Id} cannot be assigned by {this.Context.User.Username}# {this.Context.User.Discriminator}:{this.Context.User.Id}, missing role");
        }

        [RequireUserPermission(GuildPermission.Administrator, Group = "Permission")]
        [Command(nameof(RemoveAssign))]
        public async Task<RuntimeResult> RemoveAssign(SocketRole fromRole, SocketRole assignableRole)
        {
            var roles =
                await SearchTable<UserAssignableRoles>(uar =>
                    uar.PartitionKey == this.Context.Guild.Id.ToString()
                    && uar.FromRoleStorage == (long)fromRole.Id
                    && uar.TargetRoleStorage == (long)assignableRole.Id).GetCollectionAsync();

            if (!roles.Any())
            {
                return CommandResult.FromError("No roles to delete");
            }

            foreach (var role in roles)
            {
                await role.DeleteAsync();
            }

            return CommandResult.FromSuccess();
        }

        [RequireOwner]
        [Command(nameof(Error))]
        public async Task Error()
        {
            var ex = new ArgumentException();

            this._telemetry.TrackException(ex);

            await Task.CompletedTask;

            throw ex;
        }

        private async Task BindInternal(SocketRole role, string gameIdent, bool isRegExp)
        {

            var guild = await SearchTable<Guild>(g => g.RowKey == role.Guild.Id.ToString())
                .GetOneAsync();

            if (guild == null)
            {
                guild = new Guild(role.Guild);

                await guild.InsertAsync();
            }

            await guild.LoadChildrens(g => g.RoleAssignations);

            long nextOrder = 0;

            if (guild.RoleAssignations.Any())
            {
                var lastOrder = guild.RoleAssignations.Max(a => a.Order);

                nextOrder = lastOrder + 1;
            }

            var newRoleAssignation = new RoleAssignation(guild)
            {
                GameName = gameIdent,
                IsRegExp = isRegExp,
                Role = role,
                Order = nextOrder
            };

            await newRoleAssignation.InsertAsync();
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

            foreach (var roleAssignation in guild.RoleAssignations.OrderBy(ra => ra.Order).ToList())
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
