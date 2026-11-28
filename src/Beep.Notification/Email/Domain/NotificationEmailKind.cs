namespace Beep.Notification;

/// <summary><c>dbo.User_Notification_Email.Kind</c>: what a queued email is.</summary>
public enum NotificationEmailKind : byte
{
	/// <summary>The email that confirms the address (a button to the website's confirm page).</summary>
	Confirmation = 1,

	/// <summary>An alert of the subscription's type, e.g. a bank's new rates.</summary>
	Alert = 2,
}
