namespace WebsiteCollector.Worker.Infrastructure.Database
{
	public interface IWebsiteCollectorDatabase
	{
		Task Create(CancellationToken cancellationToken = default);
	}
}
