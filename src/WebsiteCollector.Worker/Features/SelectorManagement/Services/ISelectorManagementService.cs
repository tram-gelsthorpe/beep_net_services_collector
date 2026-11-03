using WebsiteCollector.Worker.Features.SelectorManagement.Models;

namespace WebsiteCollector.Worker.Features.SelectorManagement.Services;

public interface ISelectorManagementService
{
	Task<IReadOnlyList<Selector>> GetSelectorsAsync(Guid websiteId, CancellationToken ct);
}
