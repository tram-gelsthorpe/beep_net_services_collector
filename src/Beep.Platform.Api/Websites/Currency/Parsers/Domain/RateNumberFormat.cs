namespace Beep.Platform.Api.Websites.Currency.Parsers;

/// <summary>How a bank writes numbers, for <see cref="BankRateTableParser"/>.</summary>
public enum RateNumberFormat
{
	/// <summary>25,640.50 (or 25640.00)</summary>
	Comma,

	/// <summary>25.640,50</summary>
	Dot,
}
