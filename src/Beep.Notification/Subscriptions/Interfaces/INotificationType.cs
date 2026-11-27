using System.Text.Json;

namespace Beep.Notification;

/// <summary>
/// One kind of alert of one website, e.g. (Currency, BankRates). The website's project registers it; it
/// checks the subscriber's choices (the filter) and describes them in the confirmation email.
/// </summary>
public interface INotificationType
{
	/// <summary>e.g. Currency; a key of <c>Notification:Websites</c>.</summary>
	string Website { get; }

	/// <summary>e.g. BankRates.</summary>
	string Name { get; }

	/// <summary>
	/// Checks the choices and returns them as stored (JSON), or errors by field name (e.g. banks →
	/// "Choose at least one bank.").
	/// </summary>
	Task<FilterResult> NormalizeAsync(JsonElement filter, CancellationToken cancellationToken);

	/// <summary>
	/// The choices in a sentence for the confirmation email, in <paramref name="language"/>, e.g.
	/// "when Vietcombank in Vietnam publishes new rates for USD, EUR".
	/// </summary>
	Task<string> DescribeAsync(string? filter, string language, CancellationToken cancellationToken);
}
