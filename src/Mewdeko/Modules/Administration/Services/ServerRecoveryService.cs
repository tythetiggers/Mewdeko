﻿using Microsoft.EntityFrameworkCore;

namespace Mewdeko.Modules.Administration.Services;

/// <summary>
/// Service for managing server recovery.
/// </summary>
public class ServerRecoveryService : INService
{
    /// <summary>
    /// The database service.
    /// </summary>
    private readonly DbService db;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerRecoveryService"/> class.
    /// </summary>
    /// <param name="db">The database service.</param>
    public ServerRecoveryService(DbService db)
    {
        this.db = db;
    }

    /// <summary>
    /// Checks if recovery is set up for a guild.
    /// </summary>
    /// <param name="guildId">The ID of the guild to check.</param>
    /// <returns>A task that represents the asynchronous operation and contains a boolean indicating whether recovery is set up and the server recovery store.</returns>
    public async Task<(bool, ServerRecoveryStore)> RecoveryIsSetup(ulong guildId)
    {
        await using var uow = db.GetDbContext();

        var store = await uow.ServerRecoveryStore.FirstOrDefaultAsync(x => x.GuildId == guildId);
        return (store != null, store);
    }

    /// <summary>
    /// Sets up recovery for a guild.
    /// </summary>
    /// <param name="guildId">The ID of the guild to set up recovery for.</param>
    /// <param name="recoveryKey">The recovery key.</param>
    /// <param name="twoFactorKey">The two-factor key.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SetupRecovery(ulong guildId, string recoveryKey, string twoFactorKey)
    {
        await using var uow = db.GetDbContext();

        var toAdd = new ServerRecoveryStore
        {
            GuildId = guildId, RecoveryKey = recoveryKey, TwoFactorKey = twoFactorKey
        };

        await uow.ServerRecoveryStore.AddAsync(toAdd);
        await uow.SaveChangesAsync();
    }

    /// <summary>
    /// Clears the recovery setup for a server.
    /// </summary>
    /// <param name="serverRecoveryStore">The server recovery store to clear.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ClearRecoverySetup(ServerRecoveryStore serverRecoveryStore)
    {
        await using var uow = db.GetDbContext();
        uow.ServerRecoveryStore.Remove(serverRecoveryStore);
        await uow.SaveChangesAsync();
    }
}