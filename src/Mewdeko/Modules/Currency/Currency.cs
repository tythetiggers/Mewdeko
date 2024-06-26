﻿using System.IO;
using Discord.Commands;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko.Common.Attributes.TextCommands;
using Mewdeko.Common.TypeReaders.Models;
using Mewdeko.Modules.Currency.Services;
using Mewdeko.Modules.Currency.Services.Impl;
using SkiaSharp;

namespace Mewdeko.Modules.Currency;

/// <summary>
///     Module for managing currency.
/// </summary>
/// <param name="interactive"></param>
public class Currency(InteractiveService interactive, BlackjackService blackjackService)
    : MewdekoModuleBase<ICurrencyService>
{
    /// <summary>
    ///     Checks the current balance of the user.
    /// </summary>
    /// <example>.$</example>
    [Cmd]
    [Aliases]
    public async Task Cash()
    {
        var eb = new EmbedBuilder()
            .WithOkColor()
            .WithDescription(
                $"Your current balance is: {await Service.GetUserBalanceAsync(ctx.User.Id, ctx.Guild.Id)} {await Service.GetCurrencyEmote(ctx.Guild.Id)}");

        await ReplyAsync(embed: eb.Build());
    }

    /// <summary>
    ///     Allows the user to flip a coin with a specified bet amount and guess.
    /// </summary>
    /// <param name="betAmount">The amount to bet.</param>
    /// <param name="guess">The user's guess ("heads" or "tails").</param>
    /// <example>.coinflip 100 heads</example>
    [Cmd]
    [Aliases]
    public async Task CoinFlip(long betAmount, string guess)
    {
        var currentBalance = await Service.GetUserBalanceAsync(ctx.User.Id, ctx.Guild.Id);
        if (betAmount > currentBalance || betAmount <= 0)
        {
            await ReplyAsync("Invalid bet amount!");
            return;
        }

        var coinFlip = new Random().Next(2) == 0 ? "heads" : "tails";
        if (coinFlip.Equals(guess, StringComparison.OrdinalIgnoreCase))
        {
            await Service.AddUserBalanceAsync(ctx.User.Id, betAmount, ctx.Guild.Id);
            await Service.AddTransactionAsync(ctx.User.Id, betAmount, "Won Coin Flip", ctx.Guild.Id);
            await ReplyAsync(
                $"It was {coinFlip}! You won {betAmount} {await Service.GetCurrencyEmote(ctx.Guild.Id)}!");
        }
        else
        {
            await Service.AddUserBalanceAsync(ctx.User.Id, -betAmount, ctx.Guild.Id);
            await Service.AddTransactionAsync(ctx.User.Id, -betAmount, "Lost Coin Flip", ctx.Guild.Id);
            await ReplyAsync(
                $"It was {coinFlip}. You lost {betAmount} {await Service.GetCurrencyEmote(ctx.Guild.Id)}.");
        }
    }


    /// <summary>
    /// Adds money to a users balance
    /// </summary>
    /// <param name="amount">The amount to add, dont go too crazy lol. Can be negative</param>
    /// <param name="reason">The reason you are doing this to this, person, thing, whatever</param>
    [Cmd, Aliases, CurrencyPermissions]
    public async Task ModifyBalance(IUser user, long amount, [Remainder]string? reason = null)
    {
        await Service.AddUserBalanceAsync(user.Id, amount, ctx.Guild.Id);
        await Service.AddTransactionAsync(user.Id, amount, reason, ctx.Guild.Id);
        await ReplyConfirmLocalizedAsync("user_balance_modified", user.Mention, amount, reason);
    }

    /// <summary>
    ///     Allows the user to claim their daily reward.
    /// </summary>
    /// <example>.dailyreward</example>
    [Cmd]
    [Aliases]
    public async Task DailyReward()
    {
        var (rewardAmount, cooldownSeconds) = await Service.GetReward(ctx.Guild.Id);
        if (rewardAmount == 0)
        {
            await ctx.Channel.SendErrorAsync("Daily reward is not set up.", Config);
            return;
        }

        var minimumTimeBetweenClaims = TimeSpan.FromSeconds(cooldownSeconds);

        var recentTransactions = (await Service.GetTransactionsAsync(ctx.User.Id, ctx.Guild.Id))
            .Where(t => t.Description == "Daily Reward" &&
                        t.DateAdded > DateTime.UtcNow - minimumTimeBetweenClaims);

        if (recentTransactions.Any())
        {
            var nextAllowedClaimTime = recentTransactions.Max(t => t.DateAdded) + minimumTimeBetweenClaims;

            await ctx.Channel.SendErrorAsync(
                $"You already claimed your daily reward. Come back at {TimestampTag.FromDateTime(nextAllowedClaimTime.Value)}",
                Config);
            return;
        }

        await Service.AddUserBalanceAsync(ctx.User.Id, rewardAmount, ctx.Guild.Id);
        await Service.AddTransactionAsync(ctx.User.Id, rewardAmount, "Daily Reward", ctx.Guild.Id);
        await ctx.Channel.SendConfirmAsync(
            $"You claimed your daily reward of {rewardAmount} {await Service.GetCurrencyEmote(ctx.Guild.Id)}!");
    }


    /// <summary>
    ///     Allows the user to guess whether the next number is higher or lower than the current number.
    /// </summary>
    /// <param name="guess">The user's guess ("higher" or "lower").</param>
    /// <example>.highlow higher</example>
    [Cmd]
    [Aliases]
    public async Task HighLow(string guess)
    {
        var currentNumber = new Random().Next(1, 11);
        var nextNumber = new Random().Next(1, 11);

        if (guess.Equals("higher", StringComparison.OrdinalIgnoreCase) && nextNumber > currentNumber
            || guess.Equals("lower", StringComparison.OrdinalIgnoreCase) && nextNumber < currentNumber)
        {
            await Service.AddUserBalanceAsync(ctx.User.Id, 100, ctx.Guild.Id);
            await ReplyAsync(
                $"Previous number: {currentNumber}. Next number: {nextNumber}. You guessed right! You won 100 {await Service.GetCurrencyEmote(ctx.Guild.Id)}!");
        }
        else
        {
            await Service.AddUserBalanceAsync(ctx.User.Id, -100, ctx.Guild.Id);
            await ReplyAsync(
                $"Previous number: {currentNumber}. Next number: {nextNumber}. You guessed wrong! You lost 100 {await Service.GetCurrencyEmote(ctx.Guild.Id)}.");
        }
    }

    /// <summary>
    ///     Displays the leaderboard of users with the highest balances.
    /// </summary>
    /// <example>.leaderboard</example>
    [Cmd]
    [Aliases]
    public async Task Leaderboard()
    {
        var users = (await Service.GetAllUserBalancesAsync(ctx.Guild.Id))
            .OrderByDescending(u => u.Balance)
            .ToList();

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex((users.Count - 1) / 10)
            .WithDefaultEmotes()
            .WithActionOnCancellation(ActionOnStop.DeleteMessage)
            .Build();

        await interactive.SendPaginatorAsync(paginator, ctx.Channel, TimeSpan.FromMinutes(60))
            .ConfigureAwait(false);

        async Task<PageBuilder> PageFactory(int index)
        {
            var pageBuilder = new PageBuilder()
                .WithTitle("Leaderboard")
                .WithDescription($"Top {users.Count} users in {ctx.Guild.Name}")
                .WithColor(Color.Blue);

            // Add the top 10 users for this page
            for (var i = index * 10; i < (index + 1) * 10 && i < users.Count; i++)
            {
                var user = await ctx.Guild.GetUserAsync(users[i].UserId) ??
                           (IUser)await ctx.Client.GetUserAsync(users[i].UserId);
                pageBuilder.AddField($"{i + 1}. {user.Username}",
                    $"{users[i].Balance} {await Service.GetCurrencyEmote(ctx.Guild.Id)}", true);
            }

            return pageBuilder;
        }
    }

    /// <summary>
    ///     Sets the daily reward amount and cooldown time for the guild.
    /// </summary>
    /// <param name="amount">The amount of the daily reward.</param>
    /// <param name="time">The cooldown time for claiming the daily reward.</param>
    /// <example>.setdaily 100 1d</example>
    [Cmd]
    [Aliases]
    [UserPerm(GuildPermission.Administrator)]
    public async Task SetDaily(int amount, StoopidTime time)
    {
        await Service.SetReward(amount, time.Time.Seconds, ctx.Guild.Id);
        await ctx.Channel.SendConfirmAsync(
            $"Daily reward set to {amount} {await Service.GetCurrencyEmote(ctx.Guild.Id)} every {time.Time.Seconds} seconds.");
    }

    /// <summary>
    ///     Allows the user to spin the wheel for a chance to win or lose credits.
    /// </summary>
    /// <param name="betAmount">The amount of credits the user wants to bet.</param>
    /// <example>.spinwheel 100</example>
    [Cmd]
    [Aliases]
    [UserPerm(GuildPermission.Administrator)]
    public async Task SpinWheel(long betAmount = 0)
    {
        var balance = await Service.GetUserBalanceAsync(ctx.User.Id, ctx.Guild.Id);
        if (balance <= 0)
        {
            await ctx.Channel.SendErrorAsync(
                $"You either have no {await Service.GetCurrencyEmote(ctx.Guild.Id)} or are negative. Please do dailyreward and try again.",
                Config);
            return;
        }

        if (betAmount > balance)
        {
            await ctx.Channel.SendErrorAsync(
                $"You don't have enough {await Service.GetCurrencyEmote(ctx.Guild.Id)} to place that bet.", Config);
            return;
        }

        string[] segments =
        [
            "-$10", "-10%", "+$10", "+30%", "+$30", "-5%"
        ];
        int[] weights =
        [
            2, 2, 1, 1, 1, 2
        ];
        var rand = new Random();
        var winningSegment = GenerateWeightedRandomSegment(segments.Length, weights, rand);

        // Prepare the wheel image
        using var bitmap = new SKBitmap(500, 500);
        using var canvas = new SKCanvas(bitmap);
        DrawWheel(canvas, segments.Length, segments, winningSegment + 2); // Adjust the index as needed

        using var stream = new MemoryStream();
        bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
        stream.Seek(0, SeekOrigin.Begin);

        var balanceChange = await ComputeBalanceChange(segments[winningSegment], betAmount);
        if (segments[winningSegment].StartsWith("+"))
        {
            balanceChange += betAmount;
        }
        else if (segments[winningSegment].StartsWith("-"))
        {
            balanceChange = betAmount - Math.Abs(balanceChange);
        }

        // Update user balance
        await Service.AddUserBalanceAsync(ctx.User.Id, balanceChange, ctx.Guild.Id);
        await Service.AddTransactionAsync(ctx.User.Id, balanceChange,
            $"Wheel Spin {(segments[winningSegment].Contains('-') ? "Loss" : "Win")}", ctx.Guild.Id);

        var eb = new EmbedBuilder()
            .WithTitle(balanceChange > 0 ? "You won!" : "You lost!")
            .WithDescription(
                $"Result: {segments[winningSegment]}. Your balance changed by {balanceChange} {await Service.GetCurrencyEmote(ctx.Guild.Id)}")
            .WithColor(balanceChange > 0 ? Color.Green : Color.Red)
            .WithImageUrl("attachment://wheelResult.png");

        // Send the image and embed as a message to the channel
        await ctx.Channel.SendFileAsync(stream, "wheelResult.png", embed: eb.Build());

        // Helper method to generate weighted random segment
        int GenerateWeightedRandomSegment(int segmentCount, int[] segmentWeights, Random random)
        {
            var totalWeight = segmentWeights.Sum();
            var randomNumber = random.Next(totalWeight);

            var accumulatedWeight = 0;
            for (var i = 0; i < segmentCount; i++)
            {
                accumulatedWeight += segmentWeights[i];
                if (randomNumber < accumulatedWeight)
                    return i;
            }

            return segmentCount - 1; // Return the last segment as a fallback
        }

        // Helper method to compute balance change
        Task<long> ComputeBalanceChange(string segment, long betAmount)
        {
            long balanceChange = 0;

            if (segment.EndsWith("%"))
            {
                var percent = int.Parse(segment.Substring(1, segment.Length - 2));
                var portion = (long)Math.Ceiling(betAmount * (percent / 100.0));
                balanceChange = segment.StartsWith("-") ? -portion : portion;
            }
            else
            {
                var val = int.Parse(segment.Replace("$", "").Replace("+", "").Replace("-", ""));
                balanceChange = segment.StartsWith("-") ? -val : val;
            }

            return Task.FromResult(balanceChange);
        }
    }

    /// <summary>
    ///     Starts a new game of Blackjack or joins an existing one.
    /// </summary>
    /// <param name="amount">The bet amount for the player.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Cmd]
    [Aliases]
    public async Task Blackjack(long amount)
    {
        var currentBalance = await Service.GetUserBalanceAsync(ctx.User.Id, ctx.Guild.Id);
        if (amount > currentBalance || amount <= 0)
        {
            await ReplyAsync("Invalid bet amount!");
            return;
        }

        try
        {
            var game = BlackjackService.StartOrJoinGame(ctx.User, amount);
            var embed = game.CreateGameEmbed($"{ctx.User.Username} joined the game!");
            await ReplyAsync(embeds: embed);
        }
        catch (InvalidOperationException ex)
        {
            switch (ex.Message)
            {
                case "full":
                    await ReplyErrorLocalizedAsync("blackjack_game_full");
                    break;
                case "ingame":
                    await ReplyErrorLocalizedAsync("already_in_game");
                    break;
            }
        }
    }

    /// <summary>
    ///     Hits and draws a new card for the player.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Cmd]
    [Aliases]
    public async Task Hit()
    {
        try
        {
            var game = BlackjackService.GetGame(ctx.User);
            game.HitPlayer(ctx.User);
            var embed = game.CreateGameEmbed($"{ctx.User.Username} drew a card!");

            if (BlackjackService.BlackjackGame.CalculateHandTotal(game.PlayerHands[ctx.User]) > 21)
            {
                await EndGame(game, false, $"{ctx.User.Username} busted!");
            }
            else if (BlackjackService.BlackjackGame.CalculateHandTotal(game.PlayerHands[ctx.User]) == 21)
            {
                await Stand();
            }
            else
            {
                await ReplyAsync(embeds: embed);
            }
        }
        catch (InvalidOperationException ex)
        {
            await ReplyAsync(ex.Message);
        }
    }

    /// <summary>
    ///     Stands and ends the player's turn, handling the dealer's turn.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Cmd]
    [Aliases]
    public async Task Stand()
    {
        try
        {
            var embed = await blackjackService.HandleStandAsync(ctx.User,
                (userId, balanceChange) => Service.AddUserBalanceAsync(userId, balanceChange, ctx.Guild.Id),
                (userId, balanceChange, description) =>
                    Service.AddTransactionAsync(userId, balanceChange, description, ctx.Guild.Id),
                await Service.GetCurrencyEmote(ctx.Guild.Id));
            await ReplyAsync(embeds: embed);
        }
        catch (InvalidOperationException ex)
        {
            await ReplyAsync(ex.Message);
        }
    }

    /// <summary>
    ///     Ends the game and updates the player's balance and transactions.
    /// </summary>
    /// <param name="game">The current game instance.</param>
    /// <param name="playerWon">Indicates whether the player won or lost.</param>
    /// <param name="message">The message to display in the embed.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task EndGame(BlackjackService.BlackjackGame game, bool playerWon, string message)
    {
        var balanceChange = playerWon ? game.Bets[ctx.User] : -game.Bets[ctx.User];

        await Service.AddUserBalanceAsync(ctx.User.Id, balanceChange, ctx.Guild.Id);
        await Service.AddTransactionAsync(ctx.User.Id, balanceChange,
            playerWon ? "Won Blackjack" : "Lost Blackjack", ctx.Guild.Id);

        BlackjackService.EndGame(ctx.User);

        var embed = game.CreateGameEmbed(message);
        await ReplyAsync(embeds: embed);
    }


    /// <summary>
    ///     Retrieves and displays the transactions for a specified user or the current user.
    /// </summary>
    /// <param name="user">The user whose transactions are to be displayed. Defaults to the current user.</param>
    /// <example>.transactions @user</example>
    [Cmd]
    [Aliases]
    public async Task Transactions(IUser user = null)
    {
        user ??= ctx.User;

        var transactions = await Service.GetTransactionsAsync(user.Id, ctx.Guild.Id);
        transactions = transactions.OrderByDescending(x => x.DateAdded);
        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex((transactions.Count() - 1) / 10)
            .WithDefaultEmotes()
            .WithActionOnCancellation(ActionOnStop.DeleteMessage)
            .Build();

        await interactive.SendPaginatorAsync(paginator, ctx.Channel, TimeSpan.FromMinutes(60))
            .ConfigureAwait(false);

        async Task<PageBuilder> PageFactory(int index)
        {
            var pageBuilder = new PageBuilder()
                .WithTitle("Transactions")
                .WithDescription($"Transactions for {user.Username}")
                .WithColor(Color.Blue);

            for (var i = index * 10; i < (index + 1) * 10 && i < transactions.Count(); i++)
            {
                pageBuilder.AddField($"{i + 1}. {transactions.ElementAt(i).Description}",
                    $"`Amount:` {transactions.ElementAt(i).Amount} {await Service.GetCurrencyEmote(ctx.Guild.Id)}" +
                    $"\n`Date:` {TimestampTag.FromDateTime(transactions.ElementAt(i).DateAdded.Value)}");
            }

            return pageBuilder;
        }
    }

    /// <summary>
    ///     Draws a wheel with the specified number of segments and their corresponding labels, highlighting the winning
    ///     segment.
    /// </summary>
    /// <param name="canvas">The canvas on which to draw the wheel.</param>
    /// <param name="numSegments">The number of segments in the wheel.</param>
    /// <param name="segments">An array containing the labels for each segment.</param>
    /// <param name="winningSegment">The index of the winning segment (0-based).</param>
    private static void DrawWheel(SKCanvas canvas, int numSegments, string[] segments, int winningSegment)
    {
        var pastelColor = GeneratePastelColor();
        var colors = new[]
        {
            SKColors.White, pastelColor
        };

        var centerX = canvas.LocalClipBounds.MidX;
        var centerY = canvas.LocalClipBounds.MidY;
        var radius = Math.Min(centerX, centerY) - 10;

        var offsetAngle = 360f / numSegments * winningSegment;

        for (var i = 0; i < numSegments; i++)
        {
            using var paint = new SKPaint();
            paint.Style = SKPaintStyle.Fill;
            paint.Color = colors[i % colors.Length];
            paint.IsAntialias = true;

            var startAngle = i * 360 / numSegments - offsetAngle;
            var sweepAngle = 360f / numSegments;

            canvas.DrawArc(new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius),
                startAngle, sweepAngle, true, paint);
        }

        using var textPaint = new SKPaint();
        textPaint.Color = SKColors.Black;
        textPaint.TextSize = 20;
        textPaint.IsAntialias = true;
        textPaint.TextAlign = SKTextAlign.Center;

        for (var i = 0; i < numSegments; i++)
        {
            var startAngle = i * 360 / numSegments - offsetAngle;
            var middleAngle = startAngle + 360 / numSegments / 2;
            var textPosition = new SKPoint(
                centerX + radius * 0.7f * (float)Math.Cos(DegreesToRadians(middleAngle)),
                centerY + radius * 0.7f * (float)Math.Sin(DegreesToRadians(middleAngle)) +
                textPaint.TextSize / 2);

            canvas.DrawText(segments[i], textPosition.X, textPosition.Y, textPaint);
        }

        var arrowShaftLength = radius * 0.2f;
        const float arrowHeadLength = 30;
        var arrowShaftEnd = new SKPoint(centerX, centerY - arrowShaftLength);
        var arrowTip = new SKPoint(centerX, arrowShaftEnd.Y - arrowHeadLength);
        var arrowLeftSide = new SKPoint(centerX - 15, arrowShaftEnd.Y);
        var arrowRightSide = new SKPoint(centerX + 15, arrowShaftEnd.Y);

        using var arrowPaint = new SKPaint();
        arrowPaint.Style = SKPaintStyle.StrokeAndFill;
        arrowPaint.Color = SKColors.Black;
        arrowPaint.IsAntialias = true;

        var arrowPath = new SKPath();
        arrowPath.MoveTo(centerX, centerY);
        arrowPath.LineTo(arrowShaftEnd.X, arrowShaftEnd.Y);

        arrowPath.MoveTo(arrowTip.X, arrowTip.Y);
        arrowPath.LineTo(arrowLeftSide.X, arrowLeftSide.Y);
        arrowPath.LineTo(arrowRightSide.X, arrowRightSide.Y);
        arrowPath.LineTo(arrowTip.X, arrowTip.Y);

        canvas.DrawPath(arrowPath, arrowPaint);
    }


    /// <summary>
    ///     Converts degrees to radians.
    /// </summary>
    /// <param name="degrees">The angle in degrees.</param>
    /// <returns>The angle in radians.</returns>
    private static float DegreesToRadians(float degrees)
    {
        return degrees * (float)Math.PI / 180;
    }

    /// <summary>
    ///     Generates a random pastel color.
    /// </summary>
    /// <returns>The generated pastel color.</returns>
    private static SKColor GeneratePastelColor()
    {
        var rand = new Random();
        var hue = (float)rand.Next(0, 361);
        var saturation = 40f + (float)rand.NextDouble() * 20f;
        var lightness = 70f + (float)rand.NextDouble() * 20f;

        return SKColor.FromHsl(hue, saturation, lightness);
    }
}