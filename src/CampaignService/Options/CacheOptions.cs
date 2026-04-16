namespace CampaignService.Options;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public int CampaignTtlMinutes { get; init; } = 10;
}
