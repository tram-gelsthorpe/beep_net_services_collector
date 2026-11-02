using WebsiteCollector.Infrastructure.Database;
using WebsiteCollector.Features.SelectorManagement.Models;

namespace WebsiteCollector.Features.SelectorManagement.Services;

public interface ISelectorManagementService
{
	Task<IReadOnlyList<Selector>> GetSelectorsAsync(Guid websiteId, CancellationToken ct);
}

public sealed class SelectorManagementService(Database db) : ISelectorManagementService
{
	public async Task<IReadOnlyList<Selector>> GetSelectorsAsync(Guid websiteId, CancellationToken ct)
	{
		var selectors = await db.SelectorsAsync(websiteId, ct);
		return selectors.Select(s => new Selector(s.ContentSelector, s.TableSelector)).ToList();
	}
}
