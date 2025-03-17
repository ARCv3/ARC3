using Arc3.Core.Schema;
using Arc3.Core.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace Arc3.Core.Attributes;

// ReSharper disable once RedundantAttributeUsageProperty
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RequireCommandBlacklistAttribute : PreconditionAttribute
{

    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services)
    {

        var dbService = services.GetRequiredService<DbService>();
        var blacklists = await dbService.GetItemsAsync<Blacklist>("blacklist");

        var cmd = commandInfo.Name.ToLower();

        bool conditionCheck = blacklists.Any(
            blacklist =>
                MatchCurrentGuild(blacklist, context) &&
                MatchCurrentUser(blacklist, context) &&
                MatchCurrentCommand(blacklist, cmd)
        );

        if ( conditionCheck ) {
            await context.Interaction.RespondAsync("You are blacklisted from using this command.");
            return PreconditionResult.FromError(new Exception("Blacklisted"));
        }

        return PreconditionResult.FromSuccess();
  
    }

    private static bool MatchCurrentGuild(Blacklist x, IInteractionContext context) => x.GuildSnowflake == (long)context.Guild.Id || x.GuildSnowflake == 0;

    private static bool MatchCurrentUser(Blacklist x, IInteractionContext context) => x.UserSnowflake == (long)context.User.Id;

    private static bool MatchCurrentCommand(Blacklist x, String cmd) => x.Command == "all" || x.Command == cmd;

}