using Mewdeko.Common;
using Mewdeko.Services.strings;
using System.Text.RegularExpressions;

namespace Mewdeko.Modules.Utility.Services;

public class RollCommandService : INService
{
    private static readonly Regex Cleaner = new(@"[^\d]d(\d*)|^d(\d*)");
    private static readonly Regex DieFinder = new(@"(?'count'\d+)?d(?'value'\d*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OperationFinder = new(@"(?'operator'[\/\\+\-*]) *?(?'number'\d*)$", RegexOptions.Compiled);

    public static RollResult ParseRoll(string roll)
    {
        var parsed = Cleaner.Replace(roll, "1d$1$2");

        long largestPossible = 0;

        var dies = DieFinder.Matches(parsed)
                            .Select(x => new Die(int.Parse(x.Groups["count"].Value), int.Parse(x.Groups["value"].Value)))
                            .ToList();
        if (dies.Any(x => x.Sides is >= int.MaxValue or < 0))
            throw new ArgumentException("roll_fail_dice_sides");
        if (!dies.Any())
            throw new ArgumentException("roll_fail_no_dice");

        var opResult = OperationFinder.Match(parsed);

        RollResult result = new();
        try
        {
            // throw errors on int overflow.
            checked
            {
                dies.ForEach(x => largestPossible += x.Sides * x.Count);
                Random random = new();
                foreach (var d in dies)
                {
                    for (int i = 0; i < d.Count; i++)
                    {
                        var value = random.Next(d.Sides) + 1;
                        var dict = result.Results.GetValueOrDefault(d, new List<int>());
                        dict.Add(value);
                        result.Results[d] = dict;
                        result.Total += value;
                    }
                }
                if (opResult.Success)
                {
                    var op = opResult.Groups["operator"].Value.First();
                    var opVal = int.Parse(opResult.Groups["number"].Value);
                    switch (op)
                    {
                        case '+':
                            {
                                result.Total += opVal;
                                largestPossible += opVal;
                            }
                            break;
                        case '/':
                        case '\\':
                            {
                                result.Total /= opVal;
                                largestPossible /= opVal;
                            }
                            break;
                        case '*':
                            {
                                result.Total *= opVal;
                                largestPossible *= opVal;
                            }
                            break;
                        case '-':
                            {
                                result.Total -= opVal;
                                largestPossible -= opVal;
                            }
                            break;
                        default:
                            throw new NotSupportedException("unknown operation.");
                    };
                }
            }
        }
        // mark int overflow errors.
        // this still allows for individual die results, but won't produce an inaccurate total.
        catch (OverflowException)
        {
            result.InacurateTotal = true;
        }
        return result;
    }
}
