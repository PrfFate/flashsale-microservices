using System.Text.Json;
using CampaignService.Models;
using CampaignService.Options;
using CampaignService.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CampaignService.Services;

public sealed class CachedCampaignQueryService : ICampaignQueryService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ICampaignReadRepository _campaignReadRepository;
    private readonly IDistributedCache _cache;
    private readonly DistributedCacheEntryOptions _cacheEntryOptions;

    public CachedCampaignQueryService(
        ICampaignReadRepository campaignReadRepository,
        IDistributedCache cache,
        IOptions<CacheOptions> cacheOptions)
    {
        _campaignReadRepository = campaignReadRepository;
        _cache = cache;

        var ttlMinutes = Math.Max(1, cacheOptions.Value.CampaignTtlMinutes);
        _cacheEntryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttlMinutes)
        };
    }

    public async Task<IReadOnlyList<CampaignDocument>> GetCampaignsAsync(CancellationToken cancellationToken = default)
    {
        const string key = "campaigns:list";
        var cachedPayload = await _cache.GetStringAsync(key, cancellationToken);

        if (!string.IsNullOrWhiteSpace(cachedPayload))
        {
            var cachedList = JsonSerializer.Deserialize<List<CampaignDocument>>(cachedPayload, SerializerOptions);
            if (cachedList is not null)
            {
                return cachedList;
            }
        }

        var campaigns = await _campaignReadRepository.GetAllAsync(cancellationToken);
        var serialized = JsonSerializer.Serialize(campaigns, SerializerOptions);
        await _cache.SetStringAsync(key, serialized, _cacheEntryOptions, cancellationToken);

        return campaigns;
    }

    public async Task<CampaignDocument?> GetCampaignByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = $"campaigns:{id}";
        var cachedPayload = await _cache.GetStringAsync(key, cancellationToken);

        if (!string.IsNullOrWhiteSpace(cachedPayload))
        {
            return JsonSerializer.Deserialize<CampaignDocument>(cachedPayload, SerializerOptions);
        }

        var campaign = await _campaignReadRepository.GetByIdAsync(id, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var serialized = JsonSerializer.Serialize(campaign, SerializerOptions);
        await _cache.SetStringAsync(key, serialized, _cacheEntryOptions, cancellationToken);

        return campaign;
    }
}
