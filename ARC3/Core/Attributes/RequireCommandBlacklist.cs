using Arc3.Core.Schema;
using static Arc3.Core.Schema.Utils.MatchingUtils;
using Arc3.Core.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace Arc3.Core.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RequireCommandBlacklistAttribute : PreconditionAttribute
{

    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services)
    {

        var dbService = services.GetRequiredService<DbService>();
        var cmd = commandInfo.Name.ToLower();

        bool conditionCheck = await BlacklistConditionCheck(dbService, cmd, context.Guild, context.User);

        if ( conditionCheck ) {
            await context.Interaction.RespondAsync($"You are blacklisted from using {cmd}.");
            return PreconditionResult.FromError(new Exception("Blacklisted"));
        }

        return PreconditionResult.FromSuccess();

    }

    public static async Task<bool> BlacklistConditionCheck(DbService dbService, string command, IGuild guild, IUser user)
    {

        var blacklists = await dbService.GetItemsAsync<Blacklist>("blacklist");

        bool conditionCheck = blacklists.Any(
            blacklist =>
                MatchingGuild(blacklist, guild) &&
                MatchingUser(blacklist, user) &&
                MatchingCurrentOrAllCommand(blacklist, command)
        );

        return conditionCheck;
    }

    private static bool MatchingCurrentOrAllCommand(Blacklist x, String cmd) => x.Command == "all" || x.Command == cmd;

}