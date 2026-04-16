using CampaignService.Models;

namespace CampaignService.Repositories;

public interface ICampaignReadRepository
{
    Task<IReadOnlyList<CampaignDocument>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CampaignDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
