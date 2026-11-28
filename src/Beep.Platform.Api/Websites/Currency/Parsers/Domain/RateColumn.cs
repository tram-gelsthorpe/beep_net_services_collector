namespace Beep.Platform.Api.Websites.Currency.Parsers;

/// <summary>What a rate cell holds, in a bank's <see cref="BankRateTableParser.Columns"/>.</summary>
public enum RateColumn
{
	/// <summary>A cell to pass over, e.g. the currency's name after its code</summary>
	Skip,
	BuyCash,
	BuyTransfer,
	/// <summary>Bank sells (one rate for cash and transfer)</summary>
	Sell,
	SellCash,
	SellTransfer,
}
