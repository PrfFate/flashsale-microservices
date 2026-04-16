using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CampaignService.Models;

public sealed class CampaignDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; init; }

    [BsonRequired]
    public string Name { get; init; } = string.Empty;

    [BsonRequired]
    public Guid ProductId { get; init; }

    [BsonRequired]
    public decimal UnitPrice { get; init; }

    [BsonRequired]
    public int StockLimit { get; init; }

    [BsonRequired]
    public DateTime StartsAtUtc { get; init; }

    [BsonRequired]
    public DateTime EndsAtUtc { get; init; }

    [BsonIgnore]
    public bool IsActive => DateTime.UtcNow >= StartsAtUtc && DateTime.UtcNow <= EndsAtUtc;
}
