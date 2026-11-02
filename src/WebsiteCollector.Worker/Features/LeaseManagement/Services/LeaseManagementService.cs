using WebsiteCollector.Features.LeaseManagement.Models;
using WebsiteCollector.Features.WebsiteCollection.Services;

namespace WebsiteCollector.Features.LeaseManagement.Services;

public interface ILeaseManagementService
{
	Task<bool> RenewLeaseAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct);
	Task ReleaseLeaseAsync(Guid websiteSelectorsId, Guid leaseToken, CancellationToken ct);
}

public sealed class LeaseManagementService(IWebsiteCollectionService collectionService) : ILeaseManagementService
{
	public async Task<bool> RenewLeaseAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct)
	{
		try
		{
			return await collectionService.RenewLeaseAsync(websiteSelectorsId, leaseToken, leaseSeconds, ct);
		}
		catch (Exception)
		{
			return false;
		}
	}

	public async Task ReleaseLeaseAsync(Guid websiteSelectorsId, Guid leaseToken, CancellationToken ct)
	{
		// Lease is released when the collection is completed via CompleteExecutionAsync
		await Task.CompletedTask;
	}
}
