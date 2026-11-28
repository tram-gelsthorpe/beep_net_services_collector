using Beep.ServiceBus;

namespace Beep.Notification;

/// <summary>
/// The <c>Notification:Email</c> settings: how queued emails are claimed and retried through the ServiceBus.
/// </summary>
public sealed class NotificationEmailOptions : WorkerOptions
{
	public const string Name = "NotificationEmail";
	public const string Section = "Notification:Email";

	public NotificationEmailOptions()
	{
		WorkerType = Name;
		MaxConcurrentItems = 5;
		PollIntervalSeconds = 5;
		LeaseSeconds = 120;
		MaxResponseBytes = 0; // not used: emails are not HTTP responses
	}

	/// <summary>
	/// Attempts of one email, the first included, before the ServiceBus abandons it (SMTP down, mailbox full).
	/// Retries back off exponentially.
	/// </summary>
	public int MaxAttempts { get; set; } = 6;

	public override void Validate()
	{
		base.Validate();
		if (MaxAttempts < 1 || MaxAttempts > 20)
			throw new ArgumentException($"{Section}:{nameof(MaxAttempts)} must be between 1 and 20", nameof(MaxAttempts));
	}
}
