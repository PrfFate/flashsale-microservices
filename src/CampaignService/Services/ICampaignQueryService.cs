using CampaignService.Models;

namespace CampaignService.Services;

public interface ICampaignQueryService
{
    Task<IReadOnlyList<CampaignDocument>> GetCampaignsAsync(CancellationToken cancellationToken = default);
    Task<CampaignDocument?> GetCampaignByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
