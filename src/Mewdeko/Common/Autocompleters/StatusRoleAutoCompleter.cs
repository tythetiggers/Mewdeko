using Discord.Interactions;

namespace Mewdeko.Common.Autocompleters;

/// <summary>
/// Autocompleter for status roles.
/// </summary>
public class StatusRoleAutocompleter : AutocompleteHandler
{
    /// <summary>
    /// Initializes a new instance of the StatusRoleAutocompleter class.
    /// </summary>
    /// <param name="cache">The data cache.</param>
    public StatusRoleAutocompleter(IDataCache cache) => this.cache = cache;

    /// <summary>
    /// Gets the data cache.
    /// </summary>
    private readonly IDataCache cache;

    /// <summary>
    /// Generates suggestions for autocomplete.
    /// </summary>
    /// <param name="context">The interaction context.</param>
    /// <param name="interaction">The autocomplete interaction.</param>
    /// <param name="parameter">The parameter info.</param>
    /// <param name="services">The service provider.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the autocomplete result.</returns>
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction interaction, IParameterInfo parameter,
        IServiceProvider services)
    {
        var content = (string)interaction.Data.Current.Value;
        var statusRoles = await cache.GetStatusRoleCache();

        if (statusRoles == null)
            return AutocompletionResult.FromSuccess(Enumerable.Empty<AutocompleteResult>());

        return AutocompletionResult.FromSuccess(statusRoles
            .Where(x => x.GuildId == context.Guild.Id)
            .Select(x => x.Status)
            .Where(x => x.Contains(content))
            .Take(20)
            .Select(x => new AutocompleteResult(x, x)));
    }
}