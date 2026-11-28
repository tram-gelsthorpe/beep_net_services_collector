using System.Net.Mail;
using Beep.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Beep.Notification;

/// <summary>
/// Processes ServiceBus work items of type <see cref="NotificationEmailQueue.DomainType"/>: loads the queued
/// email, writes it as the subscription is now, sends it and records SentAt. A failed send is retried by the
/// ServiceBus (back-off, then abandoned after MaxAttempts); an address the mail server refuses is not retried.
/// </summary>
public sealed class NotificationEmailWorkProcessor : IWorkProcessor<IClaimedWorkItem>
{
	private readonly INotificationEmailRepository _emails;
	private readonly IReadOnlyList<INotificationType> _types;
	private readonly INotificationEmailSender _sender;
	private readonly NotificationOptions _options;
	private readonly IWorkItemRepository _workItems;
	private readonly ILogger<NotificationEmailWorkProcessor> _logger;

	public int HeartbeatIntervalSeconds { get; }

	public NotificationEmailWorkProcessor(
		INotificationEmailRepository emails,
		IEnumerable<INotificationType> types,
		INotificationEmailSender sender,
		NotificationOptions options,
		IWorkItemRepository workItems,
		NotificationEmailOptions workerOptions,
		ILogger<NotificationEmailWorkProcessor> logger)
	{
		_emails = emails ?? throw new ArgumentNullException(nameof(emails));
		_types = types?.ToList() ?? throw new ArgumentNullException(nameof(types));
		_sender = sender ?? throw new ArgumentNullException(nameof(sender));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_workItems = workItems ?? throw new ArgumentNullException(nameof(workItems));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		ArgumentNullException.ThrowIfNull(workerOptions);
		HeartbeatIntervalSeconds = Math.Max(10, workerOptions.LeaseSeconds / 3);
	}

	public Task InitializeAsync(IClaimedWorkItem item, Guid leaseToken, Guid workerId, CancellationToken ct) =>
		Task.CompletedTask;

	public async Task<ProcessingResult> ProcessAsync(IClaimedWorkItem item, Guid leaseToken, CancellationToken ct)
	{
		if (item.DomainType != NotificationEmailQueue.DomainType)
			return Failed($"Unexpected domain type '{item.DomainType}'");

		var queued = await _emails.GetAsync(item.DomainId, ct);
		if (queued is null)
			return Skipped(item, "the email no longer exists");
		if (queued.SentAt is not null)
			return Skipped(item, "already sent");

		var (email, reason) = await WriteAsync(queued, ct);
		if (email is null)
			return Skipped(item, reason!);

		try
		{
			await _sender.SendAsync(email, ct);
		}
		catch (Exception ex) when (IsRefusedAddress(ex))
		{
			// Sending again would be refused again: finish the work item, with the reason.
			_logger.LogWarning("{Kind} email {EmailId} of subscription {SubscriptionId} not sent, the address is refused: {Error}",
				queued.Kind, queued.Id, queued.Subscription.Id, ex.Message);
			return new ProcessingResult { Success = true, ErrorMessage = $"Address refused: {ex.Message}" };
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning("{Kind} email {EmailId} not sent (attempt {Attempt} of {MaxAttempts}): {Error}",
				queued.Kind, queued.Id, item.AttemptNumber, item.MaxAttempts, ex.Message);
			return Failed(ex.Message);
		}

		await _emails.MarkSentAsync(queued.Id, ct);
		return new ProcessingResult { Success = true };
	}

	public async Task CompleteAsync(IClaimedWorkItem item, Guid leaseToken, ProcessingResult result, CancellationToken ct)
	{
		var completed = await _workItems.CompleteAsync(
			item.WorkItemId, leaseToken, result.Success, result.ErrorMessage, cancellationToken: ct);

		if (!completed)
			_logger.LogWarning("Lease on email work item {WorkItemId} was lost before it could be completed", item.WorkItemId);
	}

	/// <summary>The email as the subscription is now, or why there is nothing to send.</summary>
	private async Task<(NotificationEmail? Email, string? Reason)> WriteAsync(QueuedNotificationEmail queued, CancellationToken ct)
	{
		var subscription = queued.Subscription;
		if (subscription.IsDeleted)
			return (null, "unsubscribed");
		var website = _options.FindWebsite(subscription.Website);
		if (website is null)
			return (null, $"website '{subscription.Website}' is not in Notification:Websites");
		var type = _types.FirstOrDefault(t => string.Equals(t.Website, subscription.Website, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(t.Name, subscription.NotificationType, StringComparison.OrdinalIgnoreCase));
		if (type is null)
			return (null, $"no notification type {subscription.Website} {subscription.NotificationType}");

		switch (queued.Kind)
		{
			case NotificationEmailKind.Confirmation:
				if (subscription.IsConfirmed)
					return (null, "already confirmed");
				var language = EmailText.Language(subscription.Culture);
				var description = await type.DescribeAsync(subscription.Filter, language, ct);
				return (SubscriptionService.ConfirmationEmail(subscription, website, description, language), null);

			case NotificationEmailKind.Alert:
				if (!subscription.IsConfirmed)
					return (null, "not confirmed");
				var alert = await type.AlertEmailAsync(subscription, website, queued.Data, ct);
				return alert is null ? (null, "nothing to send") : (alert, null);

			default:
				return (null, $"unknown kind {(byte)queued.Kind}");
		}
	}

	/// <summary>A malformed address, or one the mail server refuses for good (5xx).</summary>
	internal static bool IsRefusedAddress(Exception ex) => ex switch
	{
		FormatException => true,
		SmtpFailedRecipientException recipient => (int)recipient.StatusCode >= 500,
		_ => false,
	};

	private ProcessingResult Skipped(IClaimedWorkItem item, string reason)
	{
		_logger.LogInformation("Skipping email {EmailId}: {Reason}", item.DomainId, reason);
		return new ProcessingResult { Success = true };
	}

	private static ProcessingResult Failed(string error) => new() { Success = false, ErrorMessage = error };
}
