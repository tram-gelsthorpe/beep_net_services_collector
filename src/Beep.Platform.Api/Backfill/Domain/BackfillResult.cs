namespace Beep.Platform.Api.Backfill;

public sealed class BackfillResult
{
	public int DaysMissing { get; set; }
	public int DaysRecorded { get; set; }
	public int DaysWithoutRates { get; set; }
	public int RatesInserted { get; set; }
	public List<DateOnly> FailedDays { get; } = [];
	public SortedSet<string> UnknownCurrencies { get; } = new(StringComparer.OrdinalIgnoreCase);
}
