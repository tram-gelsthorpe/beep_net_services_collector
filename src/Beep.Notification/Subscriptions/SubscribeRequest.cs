using System.Text.Json;

namespace Beep.Notification;

/// <param name="Email">Subscriber's email address</param>
/// <param name="Website">e.g. Currency</param>
/// <param name="NotificationType">e.g. BankRates</param>
/// <param name="Filter">The website's choices, e.g. {"country":"VN","banks":["BFTVVNVX"],"currencies":["USD"]}</param>
/// <param name="Culture">The website's language, e.g. vi</param>
public sealed record SubscribeRequest(string? Email, string? Website, string? NotificationType, JsonElement Filter, string? Culture);
