using WebsiteCollector.Worker.Features.SelectorManagement.Models;
using WebsiteCollector.Worker.Infrastructure.Database;

namespace WebsiteCollector.Worker.Features.SelectorManagement.Services;



public sealed class SelectorManagementService(Database db) : ISelectorManagementService
{
	public async Task<IReadOnlyList<Selector>> GetSelectorsAsync(Guid websiteId, CancellationToken ct)
	{
		var selectors = await db.SelectorsAsync(websiteId, ct);
		return selectors.Select(s => new Selector(s.ContentSelector, s.TableSelector)).ToList();
	}
}
