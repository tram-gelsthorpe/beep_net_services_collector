using WebsiteCollector.Worker.Features.WebsiteCollector.Models;

namespace WebsiteCollector.Api.Repositories;

/// <summary>
/// Repository for website CRUD operations.
/// </summary>
public interface IWebsiteRepository
{
	Task<IReadOnlyList<Website>> GetAllWebsitesAsync(CancellationToken cancellationToken);
	Task<Website?> GetWebsiteByIdAsync(Guid id, CancellationToken cancellationToken);
	Task<Guid> CreateWebsiteAsync(Website website, CancellationToken cancellationToken);
	Task UpdateWebsiteAsync(Website website, CancellationToken cancellationToken);
	Task DeleteWebsiteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Repository for website selector CRUD operations.
/// </summary>
public interface ISelectorRepository
{
	Task<IReadOnlyList<WebsiteSelector>> GetSelectorsByWebsiteIdAsync(Guid websiteId, CancellationToken cancellationToken);
	Task<WebsiteSelector?> GetSelectorByIdAsync(Guid id, CancellationToken cancellationToken);
	Task<Guid> CreateSelectorAsync(WebsiteSelector selector, CancellationToken cancellationToken);
	Task UpdateSelectorAsync(WebsiteSelector selector, CancellationToken cancellationToken);
	Task DeleteSelectorAsync(Guid id, CancellationToken cancellationToken);
	Task EnableSelectorAsync(Guid id, CancellationToken cancellationToken);
	Task DisableSelectorAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Repository for collected data queries.
/// </summary>
public interface ICollectedDataRepository
{
	Task<WebsiteSelectorsCollectedData?> GetCollectedDataByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken);
	Task<WebsiteSelectorsCollectedData?> GetLatestCollectedDataBySelectorIdAsync(Guid selectorId, CancellationToken cancellationToken);
	Task<(IReadOnlyList<WebsiteSelectorsCollectedData> Items, int Total)> GetCollectionHistoryAsync(Guid selectorId, int pageNumber, int pageSize, CancellationToken cancellationToken);
	Task<WebsiteSelectorsExecutions?> GetExecutionByIdAsync(Guid executionId, CancellationToken cancellationToken);
	Task<IReadOnlyList<WebsiteSelectorsExecutions>> GetRecentExecutionsBySelectorIdAsync(Guid selectorId, int limit, CancellationToken cancellationToken);
	Task<IReadOnlyList<WebsiteSelectorsCollectedData>> SearchCollectedDataAsync(Guid? selectorId, string? searchTerm, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken);
	Task<object?> GetSelectorStatisticsAsync(Guid selectorId, CancellationToken cancellationToken);
}
