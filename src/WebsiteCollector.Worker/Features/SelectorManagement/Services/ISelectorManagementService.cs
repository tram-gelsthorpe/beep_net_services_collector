namespace WebsiteCollector.Worker;

public interface ISelectorManagementService
{
	Task<IReadOnlyList<Selector>> GetSelectorsAsync(Guid websiteId, CancellationToken ct);
}
