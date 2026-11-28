using System.Globalization;
using Beep.Platform.Api.Infrastructure.Audit;
using Beep.Platform.Api.Repositories.Currency;
using Beep.Configuration;

namespace Beep.Platform.Api.Backfill;

/// <summary>
/// <c>dotnet run --project src/Beep.Platform.Api -- backfill &lt;bank&gt;|all [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--delay-ms 1000]</c>
/// </summary>
/// <remarks>
/// Uses the Api's own settings (user secrets, environment variables, Key Vault): ConnectionStrings:Currency
/// and Audit:CreatedBy. It does not start the web server. Exit code 0 when every missing day was filled,
/// 1 when some days failed (run it again to retry them), 2 for a usage error.
/// </remarks>
public static class BackfillCommand
{
	public const string Name = "backfill";

	private const string HttpClientName = "backfill";

	private static readonly string Usage = $"""
		Usage: backfill <bank>|all [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--delay-ms 1000]
		  <bank>      {string.Join(", ", BankHistories.All.Select(h => $"{h.Name} (from {h.FirstAvailableDate:yyyy-MM-dd})"))}
		  all         each of them in turn
		  --from      first day (default and earliest: the bank's first day above)
		  --to        last day (default: yesterday in Vietnam)
		  --delay-ms  pause between requests to the bank (default: 1000)
		""";

	public static bool IsBackfill(string[] args) =>
		args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

	public static async Task<int> RunAsync(string[] args)
	{
		var banks = args.Length < 2 ? null
			: string.Equals(args[1], "all", StringComparison.OrdinalIgnoreCase) ? BankHistories.All
			: BankHistories.ForName(args[1]) is { } named ? [named]
			: null;
		if (banks is null)
		{
			Console.Error.WriteLine(Usage);
			return 2;
		}

		// The options after the bank are read as configuration (--from, --to, --delay-ms).
		var builder = WebApplication.CreateBuilder(args[2..]);
		builder.Configuration.AddKeyVaultIfConfigured();
		// One line per request from HttpClient would bury the progress lines.
		builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

		DateOnly? from;
		DateOnly to;
		int delayMs;
		try
		{
			from = ReadDate(builder.Configuration["from"]);
			to = ReadDate(builder.Configuration["to"]) ?? BankBackfill.LastCompleteDay(DateTimeOffset.UtcNow);
			delayMs = int.Parse(builder.Configuration["delay-ms"] ?? "1000", CultureInfo.InvariantCulture);
			if (delayMs < 0 || to < from)
				throw new FormatException("--to must not be before --from, and --delay-ms must not be negative");
		}
		catch (FormatException ex)
		{
			Console.Error.WriteLine(ex.Message);
			Console.Error.WriteLine(Usage);
			return 2;
		}

		builder.Configuration.RequireConnectionStrings("Currency");
		builder.Configuration.Require("Audit:CreatedBy");
		var audit = AuditOptions.FromConfiguration(builder.Configuration);

		builder.Services.AddSingleton<ICurrencyRateRepository>(
			new CurrencyRateRepository(builder.Configuration.GetConnectionString("Currency")!, audit.CreatedBy));
		builder.Services.AddHttpClient(HttpClientName, client =>
		{
			client.Timeout = TimeSpan.FromSeconds(30);
			client.DefaultRequestHeaders.UserAgent.ParseAdd("Beep.Platform.Api-Backfill/1.0");
		});

		await using var app = builder.Build();
		var logger = app.Services.GetRequiredService<ILogger<BankBackfill>>();

		using var stop = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			logger.LogWarning("Stopping after the current day; run the command again to continue");
			stop.Cancel();
		};

		var exitCode = 0;
		foreach (var bank in banks)
		{
			var bankFrom = from is { } f && f > bank.FirstAvailableDate ? f : bank.FirstAvailableDate;
			if (to < bankFrom)
			{
				logger.LogInformation("{Bank}: nothing to fill before {First:yyyy-MM-dd}", bank.Name, bank.FirstAvailableDate);
				continue;
			}

			var backfill = new BankBackfill(
				app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
				app.Services.GetRequiredService<ICurrencyRateRepository>(),
				bank,
				logger);

			BackfillResult result;
			try
			{
				result = await backfill.RunAsync(bankFrom, to, TimeSpan.FromMilliseconds(delayMs), stop.Token);
			}
			catch (OperationCanceledException) when (stop.IsCancellationRequested)
			{
				logger.LogWarning("{Bank} backfill stopped", bank.Name);
				return 1;
			}

			logger.LogInformation(
				"{Bank} backfill finished: {Recorded} days recorded ({Inserted} rates), {Empty} days without rates, " +
				"{Failed} days failed{FailedDays}; unknown currencies: {Unknown}",
				bank.Name, result.DaysRecorded, result.RatesInserted, result.DaysWithoutRates, result.FailedDays.Count,
				result.FailedDays.Count == 0 ? "" : " (" + string.Join(", ", result.FailedDays.Select(d => d.ToString("yyyy-MM-dd"))) + ")",
				result.UnknownCurrencies.Count == 0 ? "none" : string.Join(", ", result.UnknownCurrencies));

			if (result.FailedDays.Count > 0)
				exitCode = 1;
		}

		return exitCode;
	}

	private static DateOnly? ReadDate(string? value) =>
		string.IsNullOrWhiteSpace(value)
			? null
			: DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
				? date
				: throw new FormatException($"'{value}' is not a date (yyyy-MM-dd)");
}
