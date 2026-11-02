using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;
using WebsiteCollector.Infrastructure.Configuration;
using WebsiteCollector.Features.WebsiteCollection.Services;
using WebsiteCollector.Features.WebsiteCollection.Models;
using WebsiteCollector.Features.SelectorManagement.Services;
using WebsiteCollector.Features.LeaseManagement.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class CollectorWorker(IWebsiteCollectionService collectionService, 
	ISelectorManagementService selectorService, 
	ILeaseManagementService leaseService, 
	IHttpClientFactory factory, 
	IOptions<CollectorOptions> settings, 
	ILogger<CollectorWorker> log) : BackgroundService
{
	private readonly Guid _worker = Guid.NewGuid();
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var opt = settings.Value;
		if (opt.Concurrency is < 1 or > 256 || opt.LeaseSeconds is < 60 or > 3600 || opt.PollSeconds < 1 || opt.MaxResponseBytes < 1 || opt.AllowedHosts.Length == 0)
			throw new InvalidOperationException("Invalid Collector settings or empty AllowedHosts");
		var running = new List<Task>();
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				foreach (var completed in running.Where(t => t.IsCompleted).ToArray())
				{
					try { await completed; }
					catch (Exception ex) { log.LogError(ex, "Job task failed unexpectedly"); }
					running.Remove(completed);
				}
				var available = opt.Concurrency - running.Count;
				if (available > 0)
				{
					var selectors = await collectionService.ClaimSelectorsAsync(_worker, Math.Min(available, 500), opt.LeaseSeconds, stoppingToken);
					foreach (var selector in selectors) running.Add(RunAsync(selector, opt, stoppingToken));
				}
				await Task.Delay(TimeSpan.FromSeconds(opt.PollSeconds), stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
			catch (Exception ex) { log.LogError(ex, "Polling failed"); await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
		}
		try { await Task.WhenAll(running); }
		catch (Exception ex) { log.LogError(ex, "One or more in-flight jobs failed during shutdown"); }
	}
	private async Task RunAsync(ClaimedWebsiteSelector selector, CollectorOptions opt, CancellationToken shutdown)
	{
		using var scope = log.BeginScope(new Dictionary<string, object> { { "WebsiteSelectorsId", selector.WebsiteSelectorsId }, { "LeaseToken", selector.LeaseToken }, { "WorkerId", _worker } });
		using var leaseLost = new CancellationTokenSource();
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(shutdown, leaseLost.Token);
		var ct = linked.Token;
		Task heartbeat = Task.CompletedTask;
		int? status = null;
		try
		{
			await collectionService.StartExecutionAsync(selector.WebsiteSelectorsId, selector.LeaseToken, _worker, ct);
			heartbeat = HeartbeatAsync(selector.WebsiteSelectorsId, selector.LeaseToken, opt, leaseLost, ct);
			if (!Uri.TryCreate(selector.CollectionUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !opt.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
				throw new InvalidOperationException("URL is not an allowed HTTPS host");
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(selector.TimeoutSeconds));
			using var response = await factory.CreateClient("collector").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			status = (int)response.StatusCode;
			response.EnsureSuccessStatusCode();
			if (response.Content.Headers.ContentLength > opt.MaxResponseBytes) throw new InvalidOperationException("Response too large");
			await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
			using var ms = new MemoryStream();
			var buffer = new byte[8192]; int n;
			while ((n = await stream.ReadAsync(buffer, timeout.Token)) > 0)
			{
				if (ms.Length + n > opt.MaxResponseBytes) throw new InvalidOperationException("Response too large");
				ms.Write(buffer, 0, n);
			}
			var raw = Encoding.UTF8.GetString(ms.ToArray());
			string? extracted = null;

			if (selector.CollectionType == "HTML")
			{
				if (!string.IsNullOrEmpty(selector.ContentSelector))
				{
					var doc = await new HtmlParser().ParseDocumentAsync(raw, ct);
					var elements = doc.QuerySelectorAll(selector.ContentSelector);

					var values = elements.Select(el => el.TextContent?.Trim()).ToList();

					extracted = JsonSerializer.Serialize(new { ContentSelector = values?.Count == 1 ? (object)values[0] : (object)values });
				}
			}
			else if (selector.CollectionType == "JSON") { using var _ = JsonDocument.Parse(raw); }
			else if (selector.CollectionType == "XML")
			{
				using var reader = System.Xml.XmlReader.Create(new StringReader(raw), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
				while (reader.Read()) { }
			}
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
			var result = await collectionService.CompleteExecutionAsync(selector.WebsiteSelectorsId, selector.LeaseToken, true, status, null, hash, raw, extracted, ct);
			if (result != 0) log.LogWarning("Lease lost before commit");
			else log.LogInformation("Website collected successfully");
		}
		catch (OperationCanceledException) when (shutdown.IsCancellationRequested || leaseLost.IsCancellationRequested)
		{ 
			log.LogWarning("Job cancelled due to shutdown or lost lease"); 
		}
		catch (Exception ex)
		{
			log.LogError(ex, "Website collection failed");
			try { await collectionService.CompleteExecutionAsync(selector.WebsiteSelectorsId, selector.LeaseToken, false, status, ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message, null, null, null, ct); }
			catch (Exception dbEx) { log.LogError(dbEx, "Could not record failure"); }
		}
		finally
		{
			await leaseLost.CancelAsync();
			try { await heartbeat; } catch (OperationCanceledException) { } catch (Exception ex) { log.LogWarning(ex, "Heartbeat ended unexpectedly"); }
		}
	}
	private async Task HeartbeatAsync(Guid websiteSelectorsId, Guid leaseToken, CollectorOptions opt, CancellationTokenSource lost, CancellationToken ct)
	{
		try
		{
			while (!ct.IsCancellationRequested)
			{
				await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, opt.LeaseSeconds / 3)), ct);
				if (!await leaseService.RenewLeaseAsync(websiteSelectorsId, leaseToken, opt.LeaseSeconds, ct))
				{
					log.LogWarning("Lease renewal rejected");
					await lost.CancelAsync(); return;
				}
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
		catch (Exception ex) { log.LogError(ex, "Lease heartbeat failed"); await lost.CancelAsync(); }
	}
}
