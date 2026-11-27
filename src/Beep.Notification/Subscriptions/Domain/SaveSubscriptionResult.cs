namespace Beep.Notification;

/// <param name="Subscription">The subscription as saved</param>
/// <param name="IsNewUser">A <c>dbo.User</c> was created for the email address</param>
public sealed record SaveSubscriptionResult(Subscription Subscription, bool IsNewUser);
