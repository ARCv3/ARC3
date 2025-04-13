using Arc3.Core.Schema.Commons;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Arc3.Core.Schema;

public class GuildInfo : IStoresGuild
{

  [BsonElement("_id"), BsonId] public ObjectId _id;
  
  [BsonElement("guildsnowflake")]
  public long GuildSnowflake { get; set; }

  [BsonElement("premium")]
  public bool Premium { get; set; }

  [BsonElement("moderators")]
  public List<string> Moderators { get; set; }
  
  [BsonElement("ownerid")]
  public long OwnerId { get; set; }

}
