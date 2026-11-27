namespace Beep.Notification;

/// <summary>
/// Subscribers (<c>dbo.User</c>) and their subscriptions (<c>dbo.User_Notification</c>) in
/// A_1_Beep_Global_Default.
/// </summary>
public interface ISubscriptionRepository
{
	/// <summary>
	/// Saves what <paramref name="email"/> subscribes to. A user of the licence with that address is reused;
	/// otherwise one is created with a new 9-digit login. An existing subscription of the same website and type
	/// gets the new filter and is active again (if it was unsubscribed); it stays confirmed if it was.
	/// </summary>
	Task<SaveSubscriptionResult> SaveAsync(
		string email, string? culture, string website, string notificationType, string? filter,
		CancellationToken cancellationToken);

	Task<Subscription?> FindByTokenAsync(string token, CancellationToken cancellationToken);

	/// <summary>Marks the subscription confirmed and active. Null when the token is unknown.</summary>
	Task<Subscription?> ConfirmAsync(string token, CancellationToken cancellationToken);

	/// <summary>Marks the subscription unsubscribed. Null when the token is unknown.</summary>
	Task<Subscription?> UnsubscribeAsync(string token, CancellationToken cancellationToken);

	/// <summary>Confirmed, not unsubscribed subscriptions of active users, for one website and type.</summary>
	Task<IReadOnlyList<Subscription>> GetActiveAsync(string website, string notificationType, CancellationToken cancellationToken);

	/// <summary>Records that an email was just sent for these subscriptions.</summary>
	Task MarkSentAsync(IReadOnlyCollection<Guid> subscriptionIds, CancellationToken cancellationToken);
}
