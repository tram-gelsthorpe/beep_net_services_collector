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

	/// <summary>
	/// The alert email of a confirmed subscription, written when it is sent (also on a retry), in the subscriber's
	/// language. <paramref name="data"/> is what the alert was queued with (<see cref="INotificationEmailQueue"/>),
	/// e.g. <c>{"bank":"BFTVVNVX"}</c>. Null when there is nothing to send, e.g. none of the chosen currencies has rates.
	/// </summary>
	Task<NotificationEmail?> AlertEmailAsync(
		Subscription subscription, NotificationWebsite website, string? data, CancellationToken cancellationToken);
}
