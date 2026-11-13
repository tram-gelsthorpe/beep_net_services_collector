namespace WebsiteCollector.Worker;

public sealed class SelectorManagementService(SelectorManagementRepository db) : ISelectorManagementService
{
	public async Task<IReadOnlyList<Selector>> GetSelectorsAsync(Guid websiteId, CancellationToken ct)
	{
		var selectors = await db.GetWebsiteSelectorAsync(websiteId, ct);
		return selectors.Select(s => new Selector(s.ContentSelector, s.TableSelector)).ToList();
	}
}
