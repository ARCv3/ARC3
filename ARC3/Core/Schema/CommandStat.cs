using System.Collections;
using Arc3.Core.Schema.Commons;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Arc3.Core.Schema;

public class CommandStat : IStoresGuild {

    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; }

    [BsonElement("guild_id")]
    public long GuildSnowflake { get; set; }

    [BsonElement("args")]
    public BsonDocument Args { get; set; }

    [BsonElement("command_name")]
    public string Name { get; set; }

}