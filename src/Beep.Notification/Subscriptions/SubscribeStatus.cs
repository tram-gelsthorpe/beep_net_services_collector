namespace Beep.Notification;

public enum SubscribeStatus
{
	/// <summary>Saved; a confirmation email is on its way (or was sent in the last few minutes).</summary>
	ConfirmationSent,

	/// <summary>Saved; the address was already confirmed, so the new choices apply at once.</summary>
	Updated,

	/// <summary>Not saved: see the errors.</summary>
	Invalid,
}
