using System.Text;
using Beep.Email.Core;
using Beep.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace Beep.Notification;

/// <summary>
/// Sends with Beep.Email.Core over SMTP (<c>Mail:Smtp</c>). Without an SMTP host
/// (<c>Mail:Smtp:Network:Host</c>), each email is written to <see cref="NotificationOptions.PickupDirectory"/>
/// as an .html file instead, or dropped with a warning when that is not set either.
/// </summary>
public sealed class NotificationEmailSender(ISender sender, ISettings mail, NotificationOptions options, ILogger<NotificationEmailSender> logger)
	: INotificationEmailSender
{
	public async Task SendAsync(NotificationEmail email, CancellationToken cancellationToken)
	{
		var smtp = mail.MailOptions.Smtp;
		if (!string.IsNullOrWhiteSpace(smtp.Network.Host))
		{
			email.From = smtp.From;
			await sender.SendAsync(email);
			logger.LogInformation("Sent email \"{Subject}\"", email.Subject);
			return;
		}

		if (string.IsNullOrWhiteSpace(options.PickupDirectory))
		{
			logger.LogWarning("Email \"{Subject}\" not sent: set Mail:Smtp:Network:Host, or Notification:PickupDirectory to keep emails as files", email.Subject);
			return;
		}

		Directory.CreateDirectory(options.PickupDirectory);
		var path = Path.Combine(options.PickupDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.html");
		var header = $"<!-- To: {string.Join(", ", email.To)} | Subject: {email.Subject} -->\n";
		await File.WriteAllTextAsync(path, header + email.Body, new UTF8Encoding(false), cancellationToken);
		logger.LogInformation("No SMTP host: email \"{Subject}\" written to {Path}", email.Subject, path);
	}
}
