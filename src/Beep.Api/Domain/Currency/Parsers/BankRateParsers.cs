namespace Beep.Api.Domain.Currency;

/// <summary>The banks whose collected rate pages the API can read, by BIC.</summary>
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
	];

	private static readonly Dictionary<string, IBankRateParser> ByName =
		All.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

	/// <summary>The parser of the bank with that endpoint name, or null when the API cannot read its page.</summary>
	public static IBankRateParser? ForName(string name) => ByName.GetValueOrDefault(name.Trim());
}
