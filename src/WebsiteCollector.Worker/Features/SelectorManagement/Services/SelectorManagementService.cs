namespace WebsiteCollector.Worker;

public sealed class SelectorManagementService(SelectorManagementRepository selectorManagementRepository) 
	: ISelectorManagementService
{
	public async Task<IReadOnlyList<Selector>> GetSelectorsAsync(Guid websiteId, CancellationToken ct)
	{
		var selectors = await selectorManagementRepository.GetWebsiteSelectorAsync(websiteId, ct);
		return selectors.Select(s => new Selector(s.ContentSelector, s.TableSelector)).ToList();
	}
}
