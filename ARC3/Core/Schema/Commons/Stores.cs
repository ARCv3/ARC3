

using MongoDB.Bson.Serialization.Attributes;

namespace Arc3.Core.Schema.Commons;

public interface IStoresGuild
{
    public long GuildSnowflake { get; set; }
}

public interface IStoresUser
{
    public long UserSnowflake { get; set; }
}

public interface IStoresChannel
{
    public long ChannelSnowflake { get; set; }
}