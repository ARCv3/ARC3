using Arc3.Core.Schema.Commons;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Arc3.Core.Schema;

public class UserNote : IStoresGuild, IStoresUser {

  [BsonId]
  [BsonRepresentation(BsonType.String)]
  public string Id { get; set; } = string.Empty;

  [BsonElement("usersnowflake")]
  public long UserSnowflake {get; set;}

  [BsonElement("guildsnowflake")]
  public long GuildSnowflake {get; set;}

  [BsonElement("note")]
  public string Note { get; set; } = string.Empty;

  [BsonElement("date")]
  public long Date { get; set; }

  [BsonElement("authorsnowflake")]
  public long AuthorSnowflake { get; set; }

}