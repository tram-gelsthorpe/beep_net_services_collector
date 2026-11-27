namespace Beep.Notification;

/// <param name="Status">What happened</param>
/// <param name="Errors">Field name → message, when invalid (email, website, notificationType, or a filter field)</param>
public sealed record SubscribeResult(SubscribeStatus Status, IReadOnlyDictionary<string, string> Errors);
