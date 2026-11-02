namespace WebsiteCollector.Infrastructure.Configuration;

public sealed class CollectorOptions
{
	public int Concurrency { get; set; } = 16;
	public int PollSeconds { get; set; } = 5;
	public int LeaseSeconds { get; set; } = 600;
	public int MaxResponseBytes { get; set; } = 2_000_000;
	public string[] AllowedHosts { get; set; } = [];
}
