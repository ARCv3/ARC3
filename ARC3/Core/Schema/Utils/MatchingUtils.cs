
using Arc3.Core.Schema.Commons;
using Discord;

namespace Arc3.Core.Schema.Utils;

public static class MatchingUtils
{

    public static bool MatchingUser(IStoresUser entity, long userId, bool and = true, bool or = false)
        => or || entity.UserSnowflake == userId && and;

    public static bool MatchingUser(IStoresUser entity, IUser user, bool and = true, bool or = false)
        => MatchingUser(entity, (long)user.Id, and, or);

    public static bool MatchingGuild(IStoresGuild entity, long guildId, bool and = true, bool or = false)
        => or || entity.GuildSnowflake == guildId && and;

    public static bool MatchingGuild(IStoresGuild entity, IGuild guild, bool and = true, bool or = false)
        => MatchingGuild(entity, (long)guild.Id, and, or);

    public static bool MatchingChannel(IStoresChannel entity, long channelId, bool and = true, bool or = false)
        => or || entity.ChannelSnowflake == channelId && and;

    public static bool MatchingChannel(IStoresChannel entity, IChannel channel, bool and = true, bool or = false)
        => MatchingChannel(entity, (long)channel.Id, and, or);

}