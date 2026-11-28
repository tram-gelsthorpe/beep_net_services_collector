using Beep.Platform.Api.Websites.Currency.Parsers;

namespace Beep.Platform.Api.Websites.Currency.Backfill;

/// <summary>The banks whose past rates can be backfilled, by <see cref="IBankHistory.Name"/>.</summary>
public static class BankHistories
{
	/// <summary>A past day's rates are its end-of-day rates, recorded at 23:00 in Vietnam.</summary>
	public static DateTimeOffset EndOfDay(DateOnly day) =>
		new(day.ToDateTime(new TimeOnly(23, 0)), BankRateParsers.VietnamOffset);

	public static IReadOnlyList<IBankHistory> All { get; } =
	[
		new VietcombankHistory(),
		new AgribankHistory(),
		new WooriHistory(),
		new BvBankHistory(),
		new NcbHistory(),
	];

	/// <summary>The history of the bank with that name, or null when it cannot be backfilled.</summary>
	public static IBankHistory? ForName(string name) =>
		All.FirstOrDefault(h => string.Equals(h.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
