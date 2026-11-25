using System.Globalization;
using Beep.Api.Infrastructure.Audit;
using Beep.Api.Repositories.Currency;
using Beep.Configuration;

namespace Beep.Api.Backfill;

/// <summary>
/// <c>dotnet run --project src/Beep.Api -- backfill vietcombank [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--delay-ms 1000]</c>
/// </summary>
/// <remarks>
/// Uses the Api's own settings (user secrets, environment variables, Key Vault): ConnectionStrings:Currency
/// and Audit:CreatedBy. It does not start the web server. Exit code 0 when every missing day was filled,
/// 1 when some days failed (run it again to retry them), 2 for a usage error.
/// </remarks>
public static class BackfillCommand
{
	public const string Name = "backfill";

	private const string Usage = """
		Usage: backfill vietcombank [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--delay-ms 1000]
		  --from      first day (default and earliest: 2020-02-01)
		  --to        last day (default: yesterday in Vietnam)
		  --delay-ms  pause between requests to Vietcombank (default: 1000)
		""";

	public static bool IsBackfill(string[] args) =>
		args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

	public static async Task<int> RunAsync(string[] args)
	{
		if (args.Length < 2 || !string.Equals(args[1], "vietcombank", StringComparison.OrdinalIgnoreCase))
		{
			Console.Error.WriteLine(Usage);
			return 2;
		}

		// The options after the bank are read as configuration (--from, --to, --delay-ms).
		var builder = WebApplication.CreateBuilder(args[2..]);
		builder.Configuration.AddKeyVaultIfConfigured();
		// One line per request from HttpClient would bury the progress lines.
		builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

		DateOnly from, to;
		int delayMs;
		try
		{
			from = ReadDate(builder.Configuration["from"]) ?? VietcombankBackfill.FirstAvailableDate;
			to = ReadDate(builder.Configuration["to"]) ?? VietcombankBackfill.LastCompleteDay(DateTimeOffset.UtcNow);
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
		builder.Services.AddHttpClient<VietcombankBackfill>(client =>
		{
			client.Timeout = TimeSpan.FromSeconds(30);
			client.DefaultRequestHeaders.UserAgent.ParseAdd("Beep.Api-Backfill/1.0");
		});

		await using var app = builder.Build();
		var logger = app.Services.GetRequiredService<ILogger<VietcombankBackfill>>();

		using var stop = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			logger.LogWarning("Stopping after the current day; run the command again to continue");
			stop.Cancel();
		};

		BackfillResult result;
		try
		{
			result = await app.Services.GetRequiredService<VietcombankBackfill>()
				.RunAsync(from, to, TimeSpan.FromMilliseconds(delayMs), stop.Token);
		}
		catch (OperationCanceledException) when (stop.IsCancellationRequested)
		{
			logger.LogWarning("Vietcombank backfill stopped");
			return 1;
		}

		logger.LogInformation(
			"Vietcombank backfill finished: {Recorded} days recorded ({Inserted} rates), {Empty} days without rates, " +
			"{Failed} days failed{FailedDays}; unknown currencies: {Unknown}",
			result.DaysRecorded, result.RatesInserted, result.DaysWithoutRates, result.FailedDays.Count,
			result.FailedDays.Count == 0 ? "" : " (" + string.Join(", ", result.FailedDays.Select(d => d.ToString("yyyy-MM-dd"))) + ")",
			result.UnknownCurrencies.Count == 0 ? "none" : string.Join(", ", result.UnknownCurrencies));

		return result.FailedDays.Count == 0 ? 0 : 1;
	}

	private static DateOnly? ReadDate(string? value) =>
		string.IsNullOrWhiteSpace(value)
			? null
			: DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
				? date
				: throw new FormatException($"'{value}' is not a date (yyyy-MM-dd)");
}
