﻿using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using Mewdeko.Modules.Gambling.Services;
using Mewdeko.Services;
using Microsoft.Extensions.DependencyInjection;
using NCalc;

namespace Mewdeko.Common.TypeReaders
{
    public class ShmartNumberTypeReader : MewdekoTypeReader<ShmartNumber>
    {
        private static readonly Regex percentRegex = new(@"^((?<num>100|\d{1,2})%)$", RegexOptions.Compiled);

        public ShmartNumberTypeReader(DiscordSocketClient client, CommandService cmds) : base(client, cmds)
        {
        }

        public override async Task<TypeReaderResult> ReadAsync(ICommandContext context, string input,
            IServiceProvider services)
        {
            await Task.Yield();

            if (string.IsNullOrWhiteSpace(input))
                return TypeReaderResult.FromError(CommandError.ParseFailed, "Input is empty.");

            var i = input.Trim().ToUpperInvariant();

            i = i.Replace("K", "000");

            //can't add m because it will conflict with max atm

            if (TryHandlePercentage(services, context, i, out var num))
                return TypeReaderResult.FromSuccess(new ShmartNumber(num, i));
            try
            {
                var expr = new Expression(i, EvaluateOptions.IgnoreCase);
                expr.EvaluateParameter += (str, ev) => EvaluateParam(str, ev, context, services);
                var lon = (long)decimal.Parse(expr.Evaluate().ToString() ?? string.Empty);
                return TypeReaderResult.FromSuccess(new ShmartNumber(lon, input));
            }
            catch (Exception)
            {
                return TypeReaderResult.FromError(CommandError.ParseFailed, $"Invalid input: {input}");
            }
        }

        private static void EvaluateParam(string name, ParameterArgs args, ICommandContext ctx, IServiceProvider svc)
        {
            switch (name.ToUpperInvariant())
            {
                case "PI":
                    args.Result = Math.PI;
                    break;
                case "E":
                    args.Result = Math.E;
                    break;
                case "ALL":
                case "ALLIN":
                    args.Result = Cur(svc, ctx);
                    break;
                case "HALF":
                    args.Result = Cur(svc, ctx) / 2;
                    break;
                case "MAX":
                    args.Result = Max(svc, ctx);
                    break;
            }
        }

        private static long Cur(IServiceProvider services, ICommandContext ctx)
        {
            var db = services.GetService<DbService>();
            long cur;
            Debug.Assert(db != null, nameof(db) + " != null");
            using var uow = db.GetDbContext();
            cur = uow.DiscordUsers.GetUserCurrency(ctx.User.Id);
            uow.SaveChanges();

            return cur;
        }

        private static long Max(IServiceProvider services, ICommandContext ctx)
        {
            var settings = services.GetService<GamblingConfigService>()?.Data;
            // ReSharper disable once PossibleNullReferenceException
            var max = settings.MaxBet;
            return max == 0
                ? Cur(services, ctx)
                : max;
        }

        private static bool TryHandlePercentage(IServiceProvider services, ICommandContext ctx, string input,
            out long num)
        {
            num = 0;
            var m = percentRegex.Match(input);
            if (m.Captures.Count == 0) return false;
            if (!long.TryParse(m.Groups["num"].ToString(), out var percent))
                return false;

            num = (long)(Cur(services, ctx) * (percent / 100.0f));
            return true;

        }
    }
}