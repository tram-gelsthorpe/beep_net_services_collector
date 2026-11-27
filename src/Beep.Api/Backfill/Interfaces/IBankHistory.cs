using System.Net;
using Beep.Api.Domain.Currency;

namespace Beep.Api.Backfill;

/// <summary>Where a bank publishes its rates of a past day, and how to read them.</summary>
public interface IBankHistory
{
	/// <summary>The bank's name in <c>backfill &lt;bank&gt;</c>, as in <see cref="IBankRateParser.Name"/>.</summary>
	string Name { get; }

	/// <summary>The bank's BIC, as in <c>dbo.Bank.Bic</c>.</summary>
	string Bic { get; }

	/// <summary>The first day the bank has rates for.</summary>
	DateOnly FirstAvailableDate { get; }

	/// <summary>
	/// True when the bank answers a day without rates (a weekend, a holiday) with the previous day's rates and
	/// no date: a day whose rates are the same as the day before's is then taken as having none.
	/// </summary>
	bool RepeatsPreviousRates { get; }

	/// <summary>The request for one day's rates.</summary>
	HttpRequestMessage CreateRequest(DateOnly day);

	/// <summary>True when a response with this (unsuccessful) status means the bank has no rates that day.</summary>
	bool MeansNoRates(HttpStatusCode status);

	/// <summary>The day's rates from a successful response; empty when the bank has none that day.</summary>
	/// <exception cref="FormatException">The response is not the bank's rates.</exception>
	IReadOnlyList<CurrencyRateBank> Parse(string body, DateOnly day);
}
