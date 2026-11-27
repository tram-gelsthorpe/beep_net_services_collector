namespace Beep.Api.Domain.Currency;

/// <summary>The banks whose collected rate pages the API can read, by BIC: Vietnam's banks, the ECB's reference
/// rates for Belgium, and SNS Bank in the Netherlands.</summary>
public static class BankRateParsers
{
	/// <summary>Vietnam's time: the rate pages' days and times are local to it.</summary>
	public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

	public static IReadOnlyList<IBankRateParser> All { get; } =
	[
		VietcombankRateParser.Instance,
		BankRateTableParser.Hsbc,
		BankRateTableParser.Woori,
		BankRateTableParser.Ocb,
		BankRateTableParser.Agribank,
		BankRateTableParser.BvBank,
		BankRateTableParser.VietABank,
		BankRateTableParser.VietinBank,
		BankRateTableParser.NamABank,
		BankRateTableParser.Ncb,
		new SeABankRateParser(),
		EcbRateParser.Instance,
		SnsRateParser.Instance,
	];

	private static readonly Dictionary<string, IBankRateParser> ByName =
		All.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> ReferenceRateBics =
		All.Where(p => p.IsReferenceRate).Select(p => p.Bic).ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>True when the bank's rates are a central bank's reference rates (<see cref="IBankRateParser.IsReferenceRate"/>).</summary>
	public static bool IsReferenceRate(string bic) => ReferenceRateBics.Contains(bic);

	/// <summary>The parser of the bank with that endpoint name, or null when the API cannot read its page.</summary>
	public static IBankRateParser? ForName(string name) => ByName.GetValueOrDefault(name.Trim());
}
