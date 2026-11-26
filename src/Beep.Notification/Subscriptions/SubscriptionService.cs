using System.Text.Json;
using Beep.Email.Core;
using Beep.Notification.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beep.Notification.Subscriptions;

/// <param name="Email">Subscriber's email address</param>
/// <param name="Website">e.g. Currency</param>
/// <param name="NotificationType">e.g. BankRates</param>
/// <param name="Filter">The website's choices, e.g. {"country":"VN","banks":["BFTVVNVX"],"currencies":["USD"]}</param>
/// <param name="Culture">The website's language, e.g. vi</param>
public sealed record SubscribeRequest(string? Email, string? Website, string? NotificationType, JsonElement Filter, string? Culture);

public enum SubscribeStatus
{
	/// <summary>Saved; a confirmation email is on its way (or was sent in the last few minutes).</summary>
	ConfirmationSent,

	/// <summary>Saved; the address was already confirmed, so the new choices apply at once.</summary>
	Updated,

	/// <summary>Not saved: see the errors.</summary>
	Invalid,
}

/// <param name="Status">What happened</param>
/// <param name="Errors">Field name → message, when invalid (email, website, notificationType, or a filter field)</param>
public sealed record SubscribeResult(SubscribeStatus Status, IReadOnlyDictionary<string, string> Errors);

/// <summary>
/// Subscribing, confirming and unsubscribing, for every website. Emails are queued (<see cref="INotificationQueue"/>).
/// </summary>
public sealed class SubscriptionService(
	ISubscriptionRepository repository,
	IEnumerable<INotificationType> types,
	NotificationOptions options,
	INotificationQueue queue,
	ILogger<SubscriptionService> logger)
{
	/// <summary>A confirmation email is not sent again within this time (repeated clicks, abuse).</summary>
	public static readonly TimeSpan ConfirmationResendDelay = TimeSpan.FromMinutes(5);

	private static readonly EmailValidator Validator = new();

	private static readonly EmailText Text = new(
	[
		["Confirm your {0} alerts", "Xác nhận thông báo {0} của bạn", "ยืนยันการแจ้งเตือน {0} ของคุณ", "确认您的 {0} 提醒", "{0} アラートの確認", "{0} 알림 확인", "Confirmez vos alertes {0}"],
		["Confirm your email address", "Xác nhận địa chỉ email", "ยืนยันที่อยู่อีเมลของคุณ", "确认您的邮箱地址", "メールアドレスの確認", "이메일 주소 확인", "Confirmez votre adresse e-mail"],
		["You asked {0} to email you {1}.", "Bạn đã yêu cầu {0} gửi email cho bạn {1}.", "คุณขอให้ {0} ส่งอีเมลถึงคุณ{1}", "您已请求 {0} 在以下情况给您发邮件：{1}。", "{0} に、{1}メールを送るよう依頼しました。", "{0}에 {1} 이메일을 보내도록 요청하셨습니다.", "Vous avez demandé à {0} de vous écrire {1}."],
		["Confirm my alerts", "Xác nhận thông báo", "ยืนยันการแจ้งเตือน", "确认提醒", "アラートを確認する", "알림 확인", "Confirmer mes alertes"],
		["If you didn’t ask for this, ignore this email: nothing will be sent.", "Nếu bạn không yêu cầu, hãy bỏ qua email này: sẽ không có email nào được gửi.", "หากคุณไม่ได้ขอ โปรดเพิกเฉยต่ออีเมลนี้ จะไม่มีการส่งอีเมลใดๆ", "如果这不是您的请求，请忽略此邮件，我们不会发送任何内容。", "心当たりがない場合は、このメールを無視してください。何も送信されません。", "요청하지 않으셨다면 이 이메일을 무시하세요. 아무것도 발송되지 않습니다.", "Si vous n’êtes pas à l’origine de cette demande, ignorez cet e-mail : rien ne sera envoyé."],
	]);

	public INotificationType? FindType(string? website, string? name) =>
		types.FirstOrDefault(t => string.Equals(t.Website, website, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

	public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken)
	{
		var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var email = request.Email?.Trim() ?? string.Empty;
		if (email.Length > 150 || !Validator.IsMailValid(email))
			errors["email"] = "Enter a valid email address.";

		var website = options.FindWebsite(request.Website);
		var type = FindType(request.Website, request.NotificationType);
		if (website is null)
			errors["website"] = $"Unknown website '{request.Website}'.";
		else if (type is null)
			errors["notificationType"] = $"Unknown notification type '{request.NotificationType}' of {website.Name}.";

		FilterResult? filter = null;
		if (type is not null)
		{
			filter = await type.NormalizeAsync(request.Filter, cancellationToken);
			foreach (var (field, message) in filter.Errors)
				errors.TryAdd(field, message);
		}

		if (errors.Count > 0)
			return new SubscribeResult(SubscribeStatus.Invalid, errors);

		var language = EmailText.Language(request.Culture);
		var saved = await repository.SaveAsync(email, language, website!.Name, type!.Name, filter!.Filter, cancellationToken);
		var subscription = saved.Subscription;
		logger.LogInformation(
			"Subscription {SubscriptionId} to {Website} {NotificationType} saved (new user: {IsNewUser}, confirmed: {IsConfirmed})",
			subscription.Id, website.Name, type.Name, saved.IsNewUser, subscription.IsConfirmed);

		if (subscription.IsConfirmed)
			return new SubscribeResult(SubscribeStatus.Updated, errors);

		if (subscription.LastSentAt is { } last && DateTimeOffset.UtcNow - last < ConfirmationResendDelay)
			return new SubscribeResult(SubscribeStatus.ConfirmationSent, errors);

		// Mark first, so quick repeats do not queue several emails.
		await repository.MarkSentAsync([subscription.Id], cancellationToken);
		queue.Enqueue($"confirmation email of subscription {subscription.Id}", async (services, ct) =>
		{
			var description = await type.DescribeAsync(subscription.Filter, language, ct);
			await services.GetRequiredService<INotificationEmailSender>()
				.SendAsync(ConfirmationEmail(subscription, website, description, language), ct);
		});
		return new SubscribeResult(SubscribeStatus.ConfirmationSent, errors);
	}

	/// <summary>The confirmation email: a button to the website's confirm page.</summary>
	public static NotificationEmail ConfirmationEmail(Subscription subscription, NotificationWebsite website, string description, string language)
	{
		var content = $"""
			<h1 style="margin:0 0 12px;font-size:22px">{EmailLayout.Encode(Text.Get("Confirm your email address", language))}</h1>
			<p style="margin:0 0 24px;font-size:15px;line-height:1.6;color:#344054">{EmailLayout.Encode(Text.Format("You asked {0} to email you {1}.", language, website.DisplayName, description))}</p>
			<p style="margin:0 0 24px">{EmailLayout.Button(website.ConfirmUrl(subscription.Token), Text.Get("Confirm my alerts", language))}</p>
			<p style="margin:0;font-size:13px;line-height:1.5;color:#667085">{EmailLayout.Encode(Text.Get("If you didn’t ask for this, ignore this email: nothing will be sent.", language))}</p>
			""";
		return new NotificationEmail(
			subscription.Email,
			Text.Format("Confirm your {0} alerts", language, website.DisplayName),
			EmailLayout.Wrap(language, website.DisplayName, content, unsubscribeUrl: null));
	}

	/// <summary>Confirms the subscription of the link's token. Null when the token is unknown.</summary>
	public Task<Subscription?> ConfirmAsync(string? token, CancellationToken cancellationToken) =>
		string.IsNullOrWhiteSpace(token) ? Task.FromResult<Subscription?>(null) : repository.ConfirmAsync(token.Trim(), cancellationToken);

	/// <summary>Unsubscribes the subscription of the link's token. Null when the token is unknown.</summary>
	public Task<Subscription?> UnsubscribeAsync(string? token, CancellationToken cancellationToken) =>
		string.IsNullOrWhiteSpace(token) ? Task.FromResult<Subscription?>(null) : repository.UnsubscribeAsync(token.Trim(), cancellationToken);
}
