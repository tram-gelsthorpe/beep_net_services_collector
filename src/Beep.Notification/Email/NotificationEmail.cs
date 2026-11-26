using Beep.Email.Core;

namespace Beep.Notification.Email;

/// <summary>An HTML email to one recipient, as Beep.Email.Core sends it.</summary>
public sealed class NotificationEmail : IEmail
{
	public NotificationEmail(string to, string subject, string htmlBody)
	{
		To = [to];
		Subject = subject;
		Body = htmlBody;
	}

	/// <summary>Set by the sender from <c>Mail:Smtp:From</c>.</summary>
	public string From { get; set; } = string.Empty;
	public IList<string> To { get; set; }
	public string Subject { get; set; }
	public string Body { get; set; }
	public IList<string> CC { get; set; } = [];
	public IList<string> BCC { get; set; } = [];
	public bool IsBodyHtml { get; set; } = true;
	public IList<string> Attachments { get; set; } = [];

	public void Dispose() { }
}
