using Arc3.Core.Schema;
using Arc3.Core.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace Arc3.Core.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePremiumAttribute : PreconditionAttribute
{

    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services)
    {

        var dbService = services.GetRequiredService<DbService>();
        var guildInfos = await dbService.GetItemsAsync<GuildInfo>("Guilds");
        var guildSnowflake = context.Guild.Id.ToString();
        var config = guildInfos.First(x => x.GuildSnowflake == guildSnowflake);

        if (config.Premium)
        {
            return PreconditionResult.FromSuccess();
        }

        await context.Interaction.RespondAsync("This feature requires " + context.Client.CurrentUser.Username + " Premium. Contact the bot owner to learn more.");
        return PreconditionResult.FromError(new Exception("NoPremium"));

    }
}