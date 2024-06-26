using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Mewdeko.Modules.RoleStates.Services;

/// <summary>
/// Provides services for managing user role states within a guild. This includes saving roles before a user leaves or is banned, and optionally restoring them upon rejoining.
/// </summary>
public class RoleStatesService : INService
{
    private readonly DbService dbService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoleStatesService"/> class.
    /// </summary>
    /// <param name="dbService">The database service to interact with stored data.</param>
    /// <param name="eventHandler">The event handler to subscribe to guild member events.</param>
    public RoleStatesService(DbService dbService, EventHandler eventHandler)
    {
        this.dbService = dbService;
        eventHandler.UserLeft += OnUserLeft;
        eventHandler.UserBanned += OnUserBanned;
        eventHandler.UserJoined += OnUserJoined;
    }

    private async Task OnUserBanned(SocketUser args, SocketGuild arsg2)
    {
        if (args is not SocketGuildUser usr) return;
        await using var db = this.dbService.GetDbContext();
        var roleStateSettings = await db.RoleStateSettings.FirstOrDefaultAsync(x => x.GuildId == arsg2.Id);
        if (roleStateSettings is null || !roleStateSettings.Enabled || !roleStateSettings.ClearOnBan) return;

        var roleState = await db.UserRoleStates.FirstOrDefaultAsync(x => x.GuildId == arsg2.Id && x.UserId == usr.Id);
        if (roleState is null) return;
        db.Remove(roleState);
        await db.SaveChangesAsync();
    }

    private async Task OnUserJoined(IGuildUser usr)
    {
        await using var db = dbService.GetDbContext();

        var roleStateSettings = await db.RoleStateSettings.FirstOrDefaultAsync(x => x.GuildId == usr.Guild.Id);
        if (roleStateSettings is null || !roleStateSettings.Enabled) return;

        if (roleStateSettings.IgnoreBots && usr.IsBot) return;

        var deniedUsers = string.IsNullOrWhiteSpace(roleStateSettings.DeniedUsers)
            ? new List<ulong>()
            : roleStateSettings.DeniedUsers.Split(',').Select(ulong.Parse).ToList();

        if (deniedUsers.Contains(usr.Id)) return;

        var roleState =
            await db.UserRoleStates.FirstOrDefaultAsync(x => x.GuildId == usr.Guild.Id && x.UserId == usr.Id);
        if (roleState is null || string.IsNullOrWhiteSpace(roleState.SavedRoles)) return;

        var savedRoleIds = roleState.SavedRoles.Split(',').Select(ulong.Parse).ToList();

        if (savedRoleIds.Any())
        {
            try
            {
                await usr.AddRolesAsync(savedRoleIds);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to assign roles to {User} in {Guild}. Most likely missing permissions\n{Exception}",
                    usr.Username, usr.Guild, ex);
            }
        }
    }


    private async Task OnUserLeft(IGuild args, IUser args2)
    {
        await using var db = this.dbService.GetDbContext();
        var roleStateSettings = await db.RoleStateSettings.FirstOrDefaultAsync(x => x.GuildId == args.Id);
        if (roleStateSettings is null || !roleStateSettings.Enabled) return;

        if (roleStateSettings.IgnoreBots && args2.IsBot) return;

        var deniedRoles = string.IsNullOrWhiteSpace(roleStateSettings.DeniedRoles)
            ? new List<ulong>()
            : roleStateSettings.DeniedRoles.Split(',').Select(ulong.Parse).ToList();

        var deniedUsers = string.IsNullOrWhiteSpace(roleStateSettings.DeniedUsers)
            ? new List<ulong>()
            : roleStateSettings.DeniedUsers.Split(',').Select(ulong.Parse).ToList();

        if (deniedUsers.Contains(args2.Id)) return;

        if (args2 is not SocketGuildUser usr) return;

        var rolesToSave = usr.Roles.Where(x => !x.IsManaged && !x.IsEveryone).Select(x => x.Id);
        if (deniedRoles.Any())
        {
            rolesToSave = rolesToSave.Except(deniedRoles);
        }

        if (!rolesToSave.Any()) return;

        var roleState = await db.UserRoleStates.FirstOrDefaultAsync(x => x.GuildId == args.Id && x.UserId == usr.Id);
        if (roleState is null)
        {
            var newRoleState = new UserRoleStates
            {
                UserName = usr.ToString(),
                GuildId = args.Id,
                UserId = usr.Id,
                SavedRoles = string.Join(",", rolesToSave),
            };
            await db.UserRoleStates.AddAsync(newRoleState);
        }
        else
        {
            roleState.SavedRoles = string.Join(",", rolesToSave);
            db.Update(roleState);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Toggles the role state feature on or off for a guild.
    /// </summary>
    /// <param name="guildId">The unique identifier of the guild.</param>
    /// <returns>A task that represents the asynchronous operation, containing a boolean indicating the operation success.</returns>
    public async Task<bool> ToggleRoleStates(ulong guildId)
    {
        await using var db = this.dbService.GetDbContext();
        var roleStateSettings = await db.RoleStateSettings.FirstOrDefaultAsync(x => x.GuildId == guildId);
        if (roleStateSettings is null)
        {
            var toAdd = new RoleStateSettings
            {
                GuildId = guildId, Enabled = true,
            };
            await db.RoleStateSettings.AddAsync(toAdd);
            await db.SaveChangesAsync();
            return true;
        }

        roleStateSettings.Enabled = !roleStateSettings.Enabled;
        db.RoleStateSettings.Update(roleStateSettings);
        await db.SaveChangesAsync();
        return roleStateSettings.Enabled;
    }

    /// <summary>
    /// Retrieves the role state settings for a guild.
    /// </summary>
    /// <param name="guildId">The unique identifier of the guild.</param>
    /// <returns>A task that represents the asynchronous operation, containing the <see cref="RoleStateSettings"/> or null if not found.</returns>
    public async Task<RoleStateSettings?> GetRoleStateSettings(ulong guildId)
    {
        await using var db = dbService.GetDbContext();
        return await db.RoleStateSettings.FirstOrDefaultAsync(x => x.GuildId == guildId) ?? null;
    }

    /// <summary>
    /// Retrieves a user's saved role state within a guild.
    /// </summary>
    /// <param name="guildId">The unique identifier of the guild.</param>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <returns>A task that represents the asynchronous operation, containing the <see cref="UserRoleStates"/> or null if not found.</returns>
    public async Task<UserRoleStates?> GetUserRoleState(ulong guildId, ulong userId)
    {
        await using var db = dbService.GetDbContext();
        return await db.UserRoleStates.FirstOrDefaultAsync(x => x.GuildId == guildId && x.UserId == userId) ?? null;
    }

    /// <summary>
    /// Retrieves all user role states within a guild.
    /// </summary>
    /// <param name="guildId">The unique identifier of the guild.</param>
    /// <returns>A task that represents the asynchronous operation, containing a list of <see cref="UserRoleStates"/>.</returns>
    public Task<List<UserRoleStates>> GetAllUserRoleStates(ulong guildId)
    {
        return dbService.GetDbContext().UserRoleStates.Where(x => x.GuildId == guildId).ToListAsync();
    }

    /// <summary>
    /// Updates the role state settings for a guild.
    /// </summary>
    /// <param name="roleStateSettings">The <see cref="RoleStateSettings"/> to be updated.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task UpdateRoleStateSettings(RoleStateSettings roleStateSettings)
    {
        await using var db = dbService.GetDbContext();
        db.RoleStateSettings.Update(roleStateSettings);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Toggles the option to ignore bots when saving and restoring roles.
    /// </summary>
    /// <param name="roleStateSettings">The <see cref="RoleStateSettings"/> to be updated.</param>
    /// <returns>A task that represents the asynchronous operation, containing a boolean indicating if bots are now ignored.</returns>
    public async Task<bool> ToggleIgnoreBots(RoleStateSettings roleStateSettings)
    {
        await using var db = dbService.GetDbContext();
        roleStateSettings.IgnoreBots = !roleStateSettings.IgnoreBots;

        db.RoleStateSettings.Update(roleStateSettings);
        await db.SaveChangesAsync();

        return roleStateSettings.IgnoreBots;
    }

    /// <summary>
    /// Toggles the option to clear saved roles upon a user's ban.
    /// </summary>
    /// <param name="roleStateSettings">The <see cref="RoleStateSettings"/> to be updated.</param>
    /// <returns>A task that represents the asynchronous operation, containing a boolean indicating if roles are cleared on ban.</returns>
    public async Task<bool> ToggleClearOnBan(RoleStateSettings roleStateSettings)
    {
        await using var db = dbService.GetDbContext();

        var previousClearOnBanValue = roleStateSettings.ClearOnBan;
        roleStateSettings.ClearOnBan = !roleStateSettings.ClearOnBan;

        db.RoleStateSettings.Update(roleStateSettings);
        await db.SaveChangesAsync();

        return roleStateSettings.ClearOnBan;
    }

    /// <summary>
    /// Adds roles to a user's saved role state.
    /// </summary>
    /// <param name="guildId">The unique identifier of the guild.</param>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="roleIds">The roles to be added.</param>
    /// <returns>A task that represents the asynchronous operation, containing a tuple with a boolean indicating success and an optional error message.</returns>
    public async Task<(bool, string)> AddRolesToUserRoleState(ulong guildId, ulong userId, IEnumerable<ulong> roleIds)
    {
        await using var db = dbService.GetDbContext();
        var userRoleState =
            await db.UserRoleStates.FirstOrDefaultAsync(x => x.GuildId == guildId && x.UserId == userId);

        if (userRoleState == null)
        {
            return (false, "No role state found for this user.");
        }

        var savedRoleIds = userRoleState.SavedRoles.Split(',').Select(ulong.Parse).ToList();
        var anyRoleAdded = false;

        foreach (var roleId in roleIds.Where(roleId => !savedRoleIds.Contains(roleId)))
        {
            savedRoleIds.Add(roleId);
            anyRoleAdded = true;
        }

        if (!anyRoleAdded)
        {
            return (false, "No roles to add.");
        }

        userRoleState.SavedRoles = string.Join(",", savedRoleIds);
        db.Update(userRoleState);
        await db.SaveChangesAsync();

        return (true, "");
    }

    /// <summary>
    /// Removes roles from a user's saved role state.
    /// </summary>
    /// <param name="guildId">The unique identifier of the guild.</param>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="roleIds">The roles to be removed.</param>
    /// <returns>A task that represents the asynchronous operation, containing a tuple with a boolean indicating success and an optional error message.</returns>
    public async Task<(bool, string)> RemoveRolesFromUserRoleState(ulong guildId, ulong userId,
        IEnumerable<ulong> roleIds)
    {
        await using var db = dbService.GetDbContext();
        var userRoleState =
            await db.UserRoleStates.FirstOrDefaultAsync(x => x.GuildId == guildId && x.UserId == userId);

        if (userRoleState == null)
        {
            return (false, "No role state found for this user.");
        }

        var savedRoleIds = userRoleState.SavedRoles.Split(',').Select(ulong.Parse).ToList();
        var anyRoleRemoved = false;

        foreach (var roleId in roleIds.Where(roleId => savedRoleIds.Contains(roleId)))
        {
            savedRoleIds.Remove(roleId);
            anyRoleRemoved = true;
        }

        if (!anyRoleRemoved)
        {
            return (false, "No roles to remove.");
        }

        userRoleState.SavedRoles = string.Join(",", savedRoleIds);
        db.Update(userRoleState);
        await db.SaveChangesAsync();

        return (true, "");
    }

    /// <summary>
    /// Deletes a user's role state.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="guildId">The unique identifier of the guild.</param>
    /// <returns>A task that represents the asynchronous operation, containing a boolean indicating if the operation was successful.</returns>
    public async Task<bool> DeleteUserRoleState(ulong userId, ulong guildId)
    {
        await using var db = dbService.GetDbContext();
        var userRoleState =
            await db.UserRoleStates.FirstOrDefaultAsync(x => x.GuildId == guildId && x.UserId == userId);
        if (userRoleState is null) return false;
        db.UserRoleStates.Remove(userRoleState);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Applies the saved role state from one user to another.
    /// </summary>
    /// <param name="sourceUserId">The source user's unique identifier.</param>
    /// <param name="targetUser">The target <see cref="IGuildUser"/>.</param>
    /// <param name="guildId">The unique identifier of the guild.</param>
    /// <returns>A task that represents the asynchronous operation, containing a boolean indicating if the operation was successful.</returns>
    public async Task<bool> ApplyUserRoleStateToAnotherUser(ulong sourceUserId, IGuildUser targetUser, ulong guildId)
    {
        await using var db = dbService.GetDbContext();

        var sourceUserRoleState =
            await db.UserRoleStates.FirstOrDefaultAsync(x => x.GuildId == guildId && x.UserId == sourceUserId);

        if (sourceUserRoleState is null || string.IsNullOrWhiteSpace(sourceUserRoleState.SavedRoles)) return false;


        var sourceUserSavedRoleIds = sourceUserRoleState.SavedRoles.Split(',').Select(ulong.Parse).ToList();
        var rolesToAssign = targetUser.Guild.Roles.Where(role => sourceUserSavedRoleIds.Contains(role.Id)).ToList();

        if (!rolesToAssign.Any()) return false;
        try
        {
            await targetUser.AddRolesAsync(rolesToAssign);
        }
        catch
        {
            Log.Error("Failed to assign roles to user {User}", targetUser.Username);
        }

        return true;
    }

    /// <summary>
    /// Sets a user's role state manually.
    /// </summary>
    /// <param name="user">The <see cref="IUser"/> whose role state is to be set.</param>
    /// <param name="guildId">The unique identifier of the guild.</param>
    /// <param name="roles">The roles to be saved.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SetRoleStateManually(IUser user, ulong guildId, IEnumerable<ulong> roles)
    {
        await using var db = dbService.GetDbContext();
        var userRoleState =
            await db.UserRoleStates.FirstOrDefaultAsync(x => x.GuildId == guildId && x.UserId == user.Id);
        if (userRoleState is null)
        {
            userRoleState = new UserRoleStates
            {
                GuildId = guildId, UserId = user.Id, UserName = user.ToString(), SavedRoles = string.Join(",", roles)
            };
            await db.UserRoleStates.AddAsync(userRoleState);
            await db.SaveChangesAsync();
        }
        else
        {
            userRoleState.SavedRoles = string.Join(",", roles);
            db.UserRoleStates.Update(userRoleState);
            await db.SaveChangesAsync();
        }
    }
}