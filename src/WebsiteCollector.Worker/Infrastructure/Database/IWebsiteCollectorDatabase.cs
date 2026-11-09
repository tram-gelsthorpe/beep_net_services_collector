namespace WebsiteCollector.Worker
{
	public interface IWebsiteCollectorDatabase
	{
		Task Create(CancellationToken cancellationToken = default);
	}
}
