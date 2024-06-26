using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko.Common.Attributes.TextCommands;
using Mewdeko.Modules.RoleStates.Services;
using Mewdeko.Services.Settings;

namespace Mewdeko.Modules.RoleStates;

/// <summary>
///
/// </summary>
/// <param name="bss">The BotConfigService instance.</param>
/// <param name="interactivity">The InteractiveService instance.</param>
public class RoleStates(BotConfigService bss, InteractiveService interactivity) : MewdekoModuleBase<RoleStatesService>
{
    /// <summary>
    /// Toggles the role states feature on or off.
    /// </summary>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task ToggleRoleStates()
    {
        if (await Service.ToggleRoleStates(ctx.Guild.Id))
            await ctx.Channel.SendConfirmAsync($"{bss.Data.SuccessEmote} Role States are now enabled!");
        else
            await ctx.Channel.SendConfirmAsync($"{bss.Data.SuccessEmote} Role States are now disabled!");
    }

    /// <summary>
    /// Toggles whether bots should be ignored by the role states feature.
    /// </summary>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task ToggleRoleStatesIgnoreBots()
    {
        var roleStateSettings = await Service.GetRoleStateSettings(ctx.Guild.Id);
        if (roleStateSettings is null)
        {
            await ctx.Channel.SendErrorAsync(
                $"{bss.Data.ErrorEmote} Role States are not enabled and have not been configured!", Config);
            return;
        }

        if (await Service.ToggleIgnoreBots(roleStateSettings))
            await ctx.Channel.SendConfirmAsync($"{bss.Data.SuccessEmote} Role States will ignore bots!");
        else
            await ctx.Channel.SendConfirmAsync($"{bss.Data.SuccessEmote} Role States will not ignore bots!");
    }


    /// <summary>
    /// Toggles whether role states should be cleared when a user is banned.
    /// </summary>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task ToggleRoleStatesClearOnBan()
    {
        var roleStateSettings = await Service.GetRoleStateSettings(ctx.Guild.Id);
        if (roleStateSettings is null)
        {
            await ctx.Channel.SendErrorAsync(
                $"{bss.Data.ErrorEmote} Role States are not enabled and have not been configured!", Config);
            return;
        }

        if (await Service.ToggleClearOnBan(roleStateSettings))
            await ctx.Channel.SendConfirmAsync($"{bss.Data.SuccessEmote} Role states will clear on ban!");
        else
            await ctx.Channel.SendConfirmAsync($"{bss.Data.SuccessEmote} Role states will not clear on ban!");
    }

    /// <summary>
    /// Displays the current settings for the role states feature.
    /// </summary>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task ViewRoleStatesSettings()
    {
        var roleStateSettings = await Service.GetRoleStateSettings(ctx.Guild.Id);
        if (roleStateSettings is null)
            await ctx.Channel.SendErrorAsync(
                $"{bss.Data.ErrorEmote} Role States are not enabled and have not been configured!", Config);
        else
        {
            var deniedUsers = string.IsNullOrWhiteSpace(roleStateSettings.DeniedUsers)
                ? new List<ulong>()
                : roleStateSettings.DeniedUsers.Split(',').Select(ulong.Parse).ToList();

            var deniedRoles = string.IsNullOrWhiteSpace(roleStateSettings.DeniedRoles)
                ? new List<ulong>()
                : roleStateSettings.DeniedRoles.Split(',').Select(ulong.Parse).ToList();


            var eb = new EmbedBuilder()
                .WithTitle("Role States Settings")
                .WithOkColor()
                .WithDescription($"`Enabled:` {roleStateSettings.Enabled}\n" +
                                 $"`Clear on ban:` {roleStateSettings.ClearOnBan}\n" +
                                 $"`Ignore bots:` {roleStateSettings.IgnoreBots}\n" +
                                 $"`Denied roles:` {(deniedRoles.Any() ? string.Join("|", deniedRoles.Select(x => $"<@&{x}>")) : "None")}\n" +
                                 $"`Denied users:` {(deniedUsers.Any() ? string.Join("|", deniedUsers.Select(x => $"<@{x}>")) : "None")}\n");
            await ctx.Channel.SendMessageAsync(embed: eb.Build());
        }
    }

    /// <summary>
    /// Displays the role states for all users.
    /// </summary>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task ViewUserRoleStates()
    {
        var userRoleStates = await Service.GetAllUserRoleStates(ctx.Guild.Id);

        if (!userRoleStates.Any())
        {
            await ctx.Channel.SendErrorAsync($"{bss.Data.ErrorEmote} No user role states have been saved!", Config);
        }
        else
        {
            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex((userRoleStates.Count - 1) / 3)
                .WithDefaultEmotes()
                .WithActionOnCancellation(ActionOnStop.DeleteMessage)
                .Build();

            await interactivity.SendPaginatorAsync(paginator, ctx.Channel, TimeSpan.FromMinutes(60))
                .ConfigureAwait(false);

            async Task<PageBuilder> PageFactory(int page)
            {
                await Task.CompletedTask.ConfigureAwait(false);

                var eb = new PageBuilder()
                    .WithTitle("User Role States")
                    .WithOkColor();

                var roleStatesToShow = userRoleStates.Skip(5 * page).Take(3).ToList();

                foreach (var userRoleState in roleStatesToShow)
                {
                    var savedRoles = string.IsNullOrWhiteSpace(userRoleState.SavedRoles)
                        ? new List<ulong>()
                        : userRoleState.SavedRoles.Split(',').Select(ulong.Parse).ToList();

                    eb.AddField($"{userRoleState.UserName} ({userRoleState.UserId})",
                        $"`Saved Roles:` {(savedRoles.Any() ? string.Join("|", savedRoles.Select(x => $"<@&{x}>")) : "None")}\n");
                }

                return eb;
            }
        }
    }

    /// <summary>
    /// Deletes the role state for a specific user.
    /// </summary>
    /// <param name="user">The user whose role state should be deleted.</param>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task DeleteUserRoleState(IGuildUser user)
    {
        var userRoleStates = await Service.DeleteUserRoleState(ctx.Guild.Id, user.Id);
        if (!userRoleStates)
            await ctx.Channel.SendErrorAsync($"{bss.Data.ErrorEmote} There is no role state for {user}!", Config);
        else
        {
            await ctx.Channel.SendConfirmAsync($"{bss.Data.SuccessEmote} User role state for {user} has been deleted!");
        }
    }

    /// <summary>
    /// Adds roles to the deny list for the role states feature.
    /// </summary>
    /// <param name="roles">The roles to be added to the deny list.</param>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task RoleStatesAddDenyRole(params IRole[] roles)
    {
        var roleStateSettings = await Service.GetRoleStateSettings(ctx.Guild.Id);

        if (roleStateSettings is null)
        {
            await ctx.Channel.SendErrorAsync(
                $"{bss.Data.ErrorEmote} Role States are not enabled and have not been configured!", Config);
            return;
        }

        var deniedRoles = string.IsNullOrWhiteSpace(roleStateSettings.DeniedRoles)
            ? new List<ulong>()
            : roleStateSettings.DeniedRoles.Split(',').Select(ulong.Parse).ToList();

        var addedCount = 0;

        foreach (var role in roles)
        {
            if (deniedRoles.Contains(role.Id)) continue;
            deniedRoles.Add(role.Id);
            addedCount++;
        }

        roleStateSettings.DeniedRoles = string.Join(",", deniedRoles);
        await Service.UpdateRoleStateSettings(roleStateSettings);

        await ctx.Channel.SendConfirmAsync(
            $"{bss.Data.SuccessEmote} Successfully added {addedCount} role(s) to the deny list.");
    }

    /// <summary>
    /// Removes roles from the deny list for the role states feature.
    /// </summary>
    /// <param name="roles">The roles to be removed from the deny list.</param>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task RoleStatesRemoveDenyRole(params IRole[] roles)
    {
        var roleStateSettings = await Service.GetRoleStateSettings(ctx.Guild.Id);

        if (roleStateSettings is null)
        {
            await ctx.Channel.SendErrorAsync(
                $"{bss.Data.ErrorEmote} Role States are not enabled and have not been configured!", Config);
            return;
        }

        var deniedRoles = string.IsNullOrWhiteSpace(roleStateSettings.DeniedRoles)
            ? new List<ulong>()
            : roleStateSettings.DeniedRoles.Split(',').Select(ulong.Parse).ToList();

        var removedCount = 0;

        foreach (var role in roles)
        {
            if (!deniedRoles.Contains(role.Id)) continue;
            deniedRoles.Remove(role.Id);
            removedCount++;
        }

        roleStateSettings.DeniedRoles = string.Join(",", deniedRoles);
        await Service.UpdateRoleStateSettings(roleStateSettings);

        await ctx.Channel.SendConfirmAsync(
            $"{bss.Data.SuccessEmote} Successfully removed {removedCount} role(s) from the deny list.");
    }

    /// <summary>
    /// Adds users to the deny list for the role states feature.
    /// </summary>
    /// <param name="users">The users to be added to the deny list.</param>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task RoleStatesAddDenyUser(params IGuildUser[] users)
    {
        var roleStateSettings = await Service.GetRoleStateSettings(ctx.Guild.Id);

        if (roleStateSettings is null)
        {
            await ctx.Channel.SendErrorAsync(
                $"{bss.Data.ErrorEmote} Role States are not enabled and have not been configured!", Config);
            return;
        }

        var deniedUsers = string.IsNullOrWhiteSpace(roleStateSettings.DeniedUsers)
            ? new List<ulong>()
            : roleStateSettings.DeniedUsers.Split(',').Select(ulong.Parse).ToList();

        var addedCount = 0;

        foreach (var user in users)
        {
            if (deniedUsers.Contains(user.Id)) continue;
            deniedUsers.Add(user.Id);
            addedCount++;
        }

        roleStateSettings.DeniedUsers = string.Join(",", deniedUsers);
        await Service.UpdateRoleStateSettings(roleStateSettings);

        await ctx.Channel.SendConfirmAsync(
            $"{bss.Data.SuccessEmote} Successfully added {addedCount} user(s) to the deny list.");
    }

    /// <summary>
    /// Removes users from the deny list for the role states feature.
    /// </summary>
    /// <param name="users">The users to be removed from the deny list.</param>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task RoleStatesRemoveDenyUser(params IGuildUser[] users)
    {
        var roleStateSettings = await Service.GetRoleStateSettings(ctx.Guild.Id);

        if (roleStateSettings is null)
        {
            await ctx.Channel.SendErrorAsync(
                $"{bss.Data.ErrorEmote} Role States are not enabled and have not been configured!", Config);
            return;
        }

        var deniedUsers = string.IsNullOrWhiteSpace(roleStateSettings.DeniedUsers)
            ? new List<ulong>()
            : roleStateSettings.DeniedUsers.Split(',').Select(ulong.Parse).ToList();

        var removedCount = 0;

        foreach (var user in users)
        {
            if (!deniedUsers.Contains(user.Id)) continue;
            deniedUsers.Remove(user.Id);
            removedCount++;
        }

        roleStateSettings.DeniedUsers = string.Join(",", deniedUsers);
        await Service.UpdateRoleStateSettings(roleStateSettings);

        await ctx.Channel.SendConfirmAsync(
            $"{bss.Data.SuccessEmote} Successfully removed {removedCount} user(s) from the deny list.");
    }

    /// <summary>
    /// Sets the role state for a specific user.
    /// </summary>
    /// <param name="user">The user whose role state should be set.</param>
    /// <param name="roles">The roles to be included in the user's role state.</param>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task SetUserRoleState(IGuildUser user, params IRole[] roles)
    {
        var roleIds = roles.Where(x => x.Id != ctx.Guild.Id && !x.IsManaged).Select(x => x.Id);
        if (!roleIds.Any())
            await ctx.Channel.SendErrorAsync($"{bss.Data.ErrorEmote} There are no valid roles specified!", Config);
        await Service.SetRoleStateManually(user, ctx.Guild.Id, roleIds);
        await ctx.Channel.SendConfirmAsync(
            $"{bss.Data.SuccessEmote} Successfully set the role state for user {user.Mention} with the specified roles.");
    }

    /// <summary>
    /// Removes roles from a user's role state.
    /// </summary>
    /// <param name="user">The user whose role state should be modified.</param>
    /// <param name="roles">The roles to be removed from the user's role state.</param>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task RemoveRolesFromRoleState(IUser user, params IRole[] roles)
    {
        var removed = await Service.RemoveRolesFromUserRoleState(ctx.Guild.Id, user.Id, roles.Select(x => x.Id));
        if (!removed.Item1)
            await ctx.Channel.SendErrorAsync($"{bss.Data.ErrorEmote} Remove failed because:\n{removed.Item2}", Config);
        else
            await ctx.Channel.SendConfirmAsync(
                $"{bss.Data.SuccessEmote} Successfully removed those roles from {user}'s Role State!.");
    }

    /// <summary>
    /// Adds roles to a user's role state.
    /// </summary>
    /// <param name="user">The user whose role state should be modified.</param>
    /// <param name="roles">The roles to be added to the user's role state.</param>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task AddRolesToRoleState(IUser user, params IRole[] roles)
    {
        var removed = await Service.AddRolesToUserRoleState(ctx.Guild.Id, user.Id, roles.Select(x => x.Id));
        if (!removed.Item1)
            await ctx.Channel.SendErrorAsync($"{bss.Data.ErrorEmote} Remove failed because:\n{removed.Item2}", Config);
        else
            await ctx.Channel.SendConfirmAsync(
                $"{bss.Data.SuccessEmote} Successfully removed those roles from {user}'s Role State!.");
    }

    /// <summary>
    /// Deletes the role state for a specific user.
    /// </summary>
    /// <param name="user">The user whose role state should be deleted.</param>
    [Cmd, Aliases, UserPerm(GuildPermission.Administrator)]
    public async Task DeleteUserRoleState(IUser user)
    {
        var deleted = await Service.DeleteUserRoleState(user.Id, ctx.Guild.Id);
        if (!deleted)
            await ctx.Channel.SendErrorAsync($"{bss.Data.ErrorEmote} No Role State to delete!", Config);
        else
            await ctx.Channel.SendConfirmAsync($"{bss.Data.SuccessEmote} Successfully deleted {user}'s Role State!");
    }
}