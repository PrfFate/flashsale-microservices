using CampaignService.Models;
using CampaignService.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CampaignService.Repositories;

public sealed class MongoCampaignReadRepository : ICampaignReadRepository
{
    private readonly IMongoCollection<CampaignDocument> _collection;

    public MongoCampaignReadRepository(
        IMongoClient mongoClient,
        IOptions<CampaignDatabaseOptions> databaseOptions)
    {
        var options = databaseOptions.Value;
        var database = mongoClient.GetDatabase(options.DatabaseName);
        _collection = database.GetCollection<CampaignDocument>(options.ProductsCollection);
    }

    public async Task<IReadOnlyList<CampaignDocument>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(Builders<CampaignDocument>.Filter.Empty)
            .ToListAsync(cancellationToken);
    }

    public async Task<CampaignDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(x => x.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
