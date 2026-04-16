namespace CampaignService.Options;

public sealed class CampaignDatabaseOptions
{
    public const string SectionName = "CampaignDatabase";

    public string DatabaseName { get; init; } = "campaigndb";
    public string ProductsCollection { get; init; } = "products";
}
