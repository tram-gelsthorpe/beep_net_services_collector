using Microsoft.Extensions.Configuration;

namespace Beep.Notification;

/// <summary>
/// The <c>Notification</c> settings: the licence subscribers are created under, and each website that
/// takes subscriptions.
/// </summary>
public sealed class NotificationOptions
{
	public const string Name = "Notification";

	/// <summary><c>dbo.Licence.Id</c> that new subscribers (<c>dbo.User</c>) are created under.</summary>
	public Guid LicenceId { get; init; }

	/// <summary>User recorded as CreatedBy / ModifiedBy.</summary>
	public Guid CreatedBy { get; init; }

	/// <summary>
	/// Where emails go when no SMTP host is set (<c>Mail:Smtp:Network:Host</c>): each email is written there as
	/// an .html file instead of being sent. For local development.
	/// </summary>
	public string? PickupDirectory { get; init; }

	/// <summary>Websites that take subscriptions, by name (e.g. Currency).</summary>
	public IReadOnlyDictionary<string, NotificationWebsite> Websites { get; init; } =
		new Dictionary<string, NotificationWebsite>(StringComparer.OrdinalIgnoreCase);

	public NotificationWebsite? FindWebsite(string? name) =>
		name is not null && Websites.TryGetValue(name, out var website) ? website : null;

	/// <summary>
	/// Reads and checks the <c>Notification</c> section; fails at startup when the licence or a website's URL
	/// is missing.
	/// </summary>
	public static NotificationOptions FromConfiguration(IConfiguration configuration, Guid createdBy)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		var section = configuration.GetSection(Name);

		if (!Guid.TryParse(section[nameof(LicenceId)], out var licenceId) || licenceId == Guid.Empty)
			throw new InvalidOperationException(
				$"{Name}:{nameof(LicenceId)} must be the dbo.Licence.Id (A_1_Beep_Global_Default) that subscribers " +
				$"are created under (environment variable {Name}__{nameof(LicenceId)}).");

		var websites = new Dictionary<string, NotificationWebsite>(StringComparer.OrdinalIgnoreCase);
		foreach (var child in section.GetSection(nameof(Websites)).GetChildren())
		{
			var url = child["Url"];
			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
				throw new InvalidOperationException(
					$"{Name}:{nameof(Websites)}:{child.Key}:Url must be the website's address, e.g. https://currency.example.com");
			websites[child.Key] = new NotificationWebsite(child.Key, child["DisplayName"] ?? child.Key, url!.TrimEnd('/'));
		}

		return new NotificationOptions
		{
			LicenceId = licenceId,
			CreatedBy = createdBy,
			PickupDirectory = section[nameof(PickupDirectory)],
			Websites = websites,
		};
	}
}

/// <param name="Name">Key used in <c>dbo.User_Notification.Website</c>, e.g. Currency</param>
/// <param name="DisplayName">Shown in emails, e.g. Beep Currency</param>
/// <param name="Url">The website's address, without a trailing slash; links in emails start with it</param>
public sealed record NotificationWebsite(string Name, string DisplayName, string Url)
{
	/// <summary>Confirmation link: the website's <c>/alerts/confirm</c> page.</summary>
	public string ConfirmUrl(string token) => $"{Url}/alerts/confirm?token={Uri.EscapeDataString(token)}";

	/// <summary>Unsubscribe link: the website's <c>/alerts/unsubscribe</c> page.</summary>
	public string UnsubscribeUrl(string token) => $"{Url}/alerts/unsubscribe?token={Uri.EscapeDataString(token)}";
}
