using Arc3.Core.Schema.Commons;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace arc3.Core.Schema;

public class KaraokeUser : IStoresUser, IStoresChannel {

  [BsonId]
  [BsonRepresentation(BsonType.String)] 
  public string Id { get; set; }

  [BsonElement("channelsnowflake")]
  public long ChannelSnowflake {get; set;}

  [BsonElement("usersnowflake")]
  public long UserSnowflake { get; set;}

  [BsonElement("rank")]
  public int Rank;

}