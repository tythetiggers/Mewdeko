﻿using Discord.Commands;
using Mewdeko.Common.Attributes.TextCommands;
using Mewdeko.Modules.Games.Common.Nunchi;
using Mewdeko.Modules.Games.Services;

namespace Mewdeko.Modules.Games;

public partial class Games
{
    /// <summary>
    /// A module containing Nunchi commands.
    /// </summary>
    /// <param name="client"></param>
    [Group]
    public class NunchiCommands(DiscordSocketClient client) : MewdekoSubmodule<GamesService>
    {
        /// <summary>
        /// Starts or joins a game of Nunchi.
        /// </summary>
        /// <example>.nunchi</example>
        [Cmd, Aliases, RequireContext(ContextType.Guild)]
        public async Task Nunchi()
        {
            var newNunchi = new NunchiGame(ctx.User.Id, ctx.User.ToString());
            NunchiGame nunchi;

            // If a game is already active
            if ((nunchi = Service.NunchiGames.GetOrAdd(ctx.Guild.Id, newNunchi)) != newNunchi)
            {
                // Join it
                if (!await nunchi.Join(ctx.User.Id, ctx.User.ToString()).ConfigureAwait(false))
                {
                    // If failed joining, the game is running or just ended
                    // await ReplyErrorLocalized("nunchi_already_started").ConfigureAwait(false);
                    return;
                }

                await ReplyConfirmLocalizedAsync("nunchi_joined", nunchi.ParticipantCount).ConfigureAwait(false);
                return;
            }

            try
            {
                await ConfirmLocalizedAsync("nunchi_created").ConfigureAwait(false);
            }
            catch
            {
                // Ignored
            }

            nunchi.OnGameEnded += NunchiOnGameEnded;
            //nunchi.OnGameStarted += Nunchi_OnGameStarted;
            nunchi.OnRoundEnded += Nunchi_OnRoundEnded;
            nunchi.OnUserGuessed += Nunchi_OnUserGuessed;
            nunchi.OnRoundStarted += Nunchi_OnRoundStarted;
            client.MessageReceived += ClientMessageReceived;

            var success = await nunchi.Initialize().ConfigureAwait(false);
            if (!success)
            {
                if (Service.NunchiGames.TryRemove(ctx.Guild.Id, out var game))
                    game.Dispose();
                await ConfirmLocalizedAsync("nunchi_failed_to_start").ConfigureAwait(false);
            }

            Task ClientMessageReceived(SocketMessage arg)
            {
                _ = Task.Run(async () =>
                {
                    if (arg.Channel.Id != ctx.Channel.Id)
                        return;

                    if (!int.TryParse(arg.Content, out var number))
                        return;
                    try
                    {
                        await nunchi.Input(arg.Author.Id, arg.Author.ToString(), number).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignored
                    }
                });
                return Task.CompletedTask;
            }

            Task NunchiOnGameEnded(NunchiGame arg1, string? arg2)
            {
                if (Service.NunchiGames.TryRemove(ctx.Guild.Id, out var game))
                {
                    client.MessageReceived -= ClientMessageReceived;
                    game.Dispose();
                }

                if (arg2 == null)
                    return ConfirmLocalizedAsync("nunchi_ended_no_winner", Format.Bold(arg2));
                return ConfirmLocalizedAsync("nunchi_ended", Format.Bold(arg2));
            }
        }

        /// <summary>
        /// Handles the event when a new round starts in the Nunchi game.
        /// </summary>
        private Task Nunchi_OnRoundStarted(NunchiGame arg, int cur) =>
            ConfirmLocalizedAsync("nunchi_round_started",
                Format.Bold(arg.ParticipantCount.ToString()),
                Format.Bold(cur.ToString()));

        /// <summary>
        /// Handles the event when a user guesses the next number in the Nunchi game.
        /// </summary>
        private Task Nunchi_OnUserGuessed(NunchiGame arg) =>
            ConfirmLocalizedAsync("nunchi_next_number", Format.Bold(arg.CurrentNumber.ToString()));

        /// <summary>
        /// Handles the event when a round ends in the Nunchi game.
        /// </summary>
        private Task Nunchi_OnRoundEnded(NunchiGame arg1, (ulong Id, string Name)? arg2)
        {
            if (arg2.HasValue)
                return ConfirmLocalizedAsync("nunchi_round_ended", Format.Bold(arg2.Value.Name));
            return ConfirmLocalizedAsync("nunchi_round_ended_boot",
                Format.Bold(
                    $"\n{string.Join("\n, ", arg1.Participants.Select(x => x.Name))}")); // this won't work if there are too many users
        }
    }
}