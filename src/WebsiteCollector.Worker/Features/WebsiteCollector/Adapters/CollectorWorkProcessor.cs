using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Beep.ServiceBus;

namespace WebsiteCollector.Worker;

/// <summary>
/// Work processor implementation for website collection
/// </summary>
internal sealed class CollectorWorkProcessor : IWorkProcessor<ClaimedWebsiteSelector>
{
	private readonly IWebsiteCollectionService _collectionService;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly WebsiteCollectorOptions _settings;
	private readonly ILogger _logger;

	public int HeartbeatIntervalSeconds { get; }

	public CollectorWorkProcessor(
		IWebsiteCollectionService collectionService,
		IHttpClientFactory httpClientFactory,
		IOptions<WebsiteCollectorOptions> settings,
		ILogger logger)
	{
		_collectionService = collectionService ?? throw new ArgumentNullException(nameof(collectionService));
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));

		HeartbeatIntervalSeconds = Math.Max(10, _settings.LeaseSeconds / 3);
	}

	public async Task InitializeAsync(ClaimedWebsiteSelector item, Guid leaseToken, Guid workerIdent, CancellationToken ct)
	{
		await _collectionService.StartExecutionAsync(item.WebsiteSelectorsId, leaseToken, workerIdent, ct);
	}

	public async Task<ProcessingResult> ProcessAsync(ClaimedWebsiteSelector item, Guid leaseToken, CancellationToken ct)
	{
		int? statusCode = null;

		try
		{
			// Validate URL
			if (!Uri.TryCreate(item.CollectionUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
			{
				return new ProcessingResult { Success = false, ErrorMessage = "URL is not an allowed HTTPS host" };
			}

			// Perform HTTP request
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(item.TimeoutSeconds));

			using var response = await _httpClientFactory.CreateClient("collector")
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

			statusCode = (int)response.StatusCode;
			response.EnsureSuccessStatusCode();

			// Read and validate response size
			if (response.Content.Headers.ContentLength > _settings.MaxResponseBytes)
				return new ProcessingResult { Success = false, ErrorMessage = "Response too large" };

			await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
			using var ms = new MemoryStream();
			var buffer = new byte[8192];
			int n;

			while ((n = await stream.ReadAsync(buffer, timeout.Token)) > 0)
			{
				if (ms.Length + n > _settings.MaxResponseBytes)
					return new ProcessingResult { Success = false, ErrorMessage = "Response too large" };

				ms.Write(buffer, 0, n);
			}

			var raw = Encoding.UTF8.GetString(ms.ToArray());
			string? extracted = null;

			// Parse content based on collection type
			if (item.CollectionType == "HTML")
			{
				if (!string.IsNullOrEmpty(item.ContentSelector))
				{
					var doc = await new HtmlParser().ParseDocumentAsync(raw, ct);
					var elements = doc.QuerySelectorAll(item.ContentSelector);
					var values = elements.Select(el => el.TextContent?.Trim()).ToList();
					extracted = JsonSerializer.Serialize(new { ContentSelector = values?.Count == 1 ? (object)values[0] : (object)values });
				}
			}
			else if (item.CollectionType == "JSON")
			{
				using var _ = JsonDocument.Parse(raw);
			}
			else if (item.CollectionType == "XML")
			{
				using var reader = System.Xml.XmlReader.Create(
					new StringReader(raw),
					new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
				while (reader.Read()) { }
			}

			// Compute hash and return success
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));

			return new ProcessingResult
			{
				Success = true,
				ResultData = new { hash, raw, extracted, statusCode }
			};
		}
		catch (Exception ex)
		{
			return new ProcessingResult
			{
				Success = false,
				ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message
			};
		}
	}

	public async Task CompleteAsync(
		ClaimedWebsiteSelector item,
		Guid leaseToken,
		ProcessingResult result,
		CancellationToken ct)
	{
		if (result.Success)
		{
			var data = result.ResultData as System.Collections.Generic.IDictionary<string, object?>;
			var hash = (byte[]?)data?["hash"];
			var raw = (string?)data?["raw"];
			var extracted = (string?)data?["extracted"];
			var statusCode = (int?)data?["statusCode"];

			var commitResult = await _collectionService.CompleteExecutionAsync(
				item.WebsiteSelectorsId,
				leaseToken,
				true,
				statusCode,
				null,
				hash,
				raw,
				extracted,
				ct);

			if (commitResult != 0)
				_logger.LogWarning("Lease lost before commit");
		}
		else
		{
			await _collectionService.CompleteExecutionAsync(
				item.WebsiteSelectorsId,
				leaseToken,
				false,
				null,
				result.ErrorMessage,
				null,
				null,
				null,
				ct);
		}
	}
}
